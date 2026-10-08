# Privacy

This kiosk resolves a person's name to a room and a time, on a screen in a
public building, from calendar data. That is personal data, and in the EU it is
GDPR territory. The design decisions below are not cosmetic — several of them
shape the API and the UI, and are expensive to retrofit.

## Decisions already implemented

**Exact-match search only.** `FindByAttendeeAsync` matches on an equality probe
against `normalized_name`. No `LIKE`, no prefix search, no autocomplete, no
"did you mean". A prefix search would turn the column into a browsable directory
of everyone with a meeting in the building — type "a" and read the lobby. Pinned
by `MeetingStoreTests.Never_matches_a_partial_name`.

**Names are POSTed, not in the query string.** A name in a URL ends up in
access logs, browser history and referrer headers.

**Attendees are not hydrated on read.** `QueryMeetingsAsync` leaves
`Attendees` empty, so the list cannot leak into a response by accident.

**A failed lookup is indistinguishable from an unknown name.** Both return an
empty result set, so the kiosk does not confirm whether a given person exists in
the tenant.

**Short retention.** `Sync:RetainPastMeetingsFor` defaults to 2 hours, and the
sync replaces the whole window each cycle. There is no reason for a machine in a
lobby to hold yesterday's attendee lists.

**Idle reset and incognito.** The UI returns to its resting view after
`Privacy:IdleTimeout` (45s by default) without touch, and Chromium runs
`--incognito`. Without both, the last visitor's name and meeting sit on the
screen until someone else walks up. The idle reset applies to the search and
result screens specifically — see below for why the agenda views are treated
differently.

**Minimal field selection.** The Graph query `$select`s only what is used, so
the app does not pull body, location, organiser or response status over the wire
at all. The organiser is deliberately *not* fetched: "Exitgesprek — J. Nap" says
more than either half alone, and a field that is never retrieved cannot leak.

**Searches are not logged.** Keep it that way. If a diagnostic log is ever
added, log the *outcome* (hit/miss) and not the name.

## The agenda views

The kiosk shows two agenda screens: *Straks*, a chronological list of upcoming
meetings across the whole building, and *Alle agenda's*, one column per room.

**This is a browse-all surface, and earlier versions of this document said there
was none.** Be clear-eyed about it: anyone standing in the hall can read what is
on, in which room, all day. What makes it defensible is what it does *not* say.

**Subjects are hidden by default, and the server is what hides them.**
`Privacy:SubjectVisibility` has three values:

| Value | Who sees meeting subjects |
|---|---|
| `Hidden` | nobody — the shipped default |
| `AdminOnly` | only a logged-in administrator |
| `Everyone` | anyone in the lobby |

With `Hidden`, an agenda row carries a time, a room and a floor. That is the
same information a printed sign on the door would carry, and it is not personal
data. Turn it up to `Everyone` and the screen becomes a public list of meeting
titles, which is a materially different thing — the admin panel says so in plain
Dutch next to the option, and it is a decision for whoever owns the building's
privacy statement, not a UI preference.

**The gate is one pure function, and it works by nulling the field.**
`Zuil.Core.Agenda.AgendaProjection.Project` is the only place the rule is
applied. It sets `Subject` to null rather than relying on the client not to
render it, because a subject that reaches the browser has already left the
building, whatever the CSS says. The store deliberately takes no privacy
parameter: gating in the data layer would spread the decision across every call
site, each free to get it wrong. `AgendaProjectionTests` pins every combination,
including that the default is `Hidden`.

`AdminOnly` exists so staff can work out why someone was sent to the wrong room
without turning the lobby screen into a noticeboard.

**The agenda views do not idle-reset.** They hold nothing a visitor typed and
nothing about any individual, so — with subjects hidden — leaving one up is
equivalent to leaving a timetable on the wall. The search and result screens
still wipe on the idle timeout, which is the case that actually matters.

**Neither agenda endpoint touches the hardware.** `GET /api/rooms/{id}/schedule`
lights an indicator as a side effect of a read, which predates this. The agenda
routes do not repeat it: the column must not swing about because somebody walked
past and glanced at the board.

## The admin panel

**The password is weak by default.** `Admin:Password` ships as `12345678`. The
mitigations are real but limited: Kestrel binds `127.0.0.1` plaintext so there
is no network attacker, login is rate-limited per IP, failures are counted in
the database so pulling the plug does not reset the lockout, and comparison is
constant-time. What none of that addresses is that the kiosk's own Chromium can
reach `/api/admin` and the touchscreen is the keyboard. **Anyone who has read
this repository can open the settings panel from the lobby until the password is
changed.** `deploy/install.sh` generates a random one; the panel shows a warning
banner and the host logs one on every start until it is no longer the default.

Corollary: **the Kestrel bind URL must never become editable from the GUI.**
Binding it to the LAN would turn a plaintext default password into a network
credential.

**The way in is deliberately obscure.** No settings button — a visitor who
finds one on a lobby kiosk will press it. The panel opens on a three-second
press in the bottom-right corner, and always to the login prompt, never to the
settings directly.

**Sessions are opaque tokens, hashed server-side**, in an `HttpOnly`
`SameSite=Strict` cookie scoped to `/api/admin`, with a sliding 15-minute
expiry. The panel also leaves on its own after two minutes of inactivity,
because a settings screen left open in a lobby is worse than an attract screen
left open. Not cookie authentication: that needs a Data Protection key ring, and
`deploy/zuil.service` sets `ProtectHome=true`, which makes Data Protection's
default key home unwritable — it would silently fall back to ephemeral keys and
sign everyone out on every restart anyway.

## The Graph credential

**It now lives in the database, encrypted.** The admin panel takes a tenant id,
a client id and a client secret; the secret is encrypted with AES-GCM against a
32-byte key in `secret.key`, beside the database, mode `0600`.

**Be honest about what that buys.** The key and the ciphertext sit on the same
disk under the same uid. It protects the credential from a database copied off
in a backup or a support dump, and from the plaintext config file it replaces.
It does **not** protect against root, against the `zuil` service account, or
against someone walking off with the SD card. Full-disk encryption, or a
certificate in a TPM, is the real answer.

**Consequences worth writing down:**

- `zuil-cache.db` now holds a credential. Backups of it are sensitive, and
  `*.db*` must stay in `.gitignore`.
- The database and `secret.key` have to be backed up *together*. Either one
  alone cannot recover the credential, and an update must clobber neither.
- The secret is write-only across the API. `GET /api/admin/settings` returns
  `secretIsSet` and no value — not even a length, which would leak something.
  `PUT` takes an explicit `secretAction` of `keep`/`set`/`clear`.
- A certificate configured in a file still wins over a stored secret, so a site
  that already follows [outlook-setup.md](outlook-setup.md) loses nothing. A
  certificate remains the better credential for an unattended machine in a
  public building.
- `GET /api/health` is unauthenticated, so the sync error it reports is a short
  sanitised description. The raw message, which for Graph failures can quote a
  response body, is behind `GET /api/admin/diagnostics`.

## Decisions still to make

- **Signage.** Whether a notice at the column is required, and in what wording,
  is a question for whoever owns the building's privacy statement. The agenda
  board makes this more pressing than it was.
- **Scope of the Entra app.** See [outlook-setup.md](outlook-setup.md): without
  an `ApplicationAccessPolicy`, the credential on this device can read every
  mailbox in the tenant. That is still the single largest risk in the whole
  system, and it matters more now that the credential is entered through a
  touchscreen in a lobby rather than placed on the device by whoever set it up.
