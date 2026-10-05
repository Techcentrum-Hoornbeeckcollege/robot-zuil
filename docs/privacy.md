# Privacy

This kiosk resolves a person's name to a room and a time, on a screen in a
public building, from calendar data. That is personal data, and in the EU it is
GDPR territory. The design decisions below are not cosmetic — several of them
shape the API and the UI, and are expensive to retrofit.

## Decisions already implemented

**Exact-match search only.** `FindByAttendeeAsync` matches on an equality probe
against `normalized_name`. No `LIKE`, no prefix search, no autocomplete, no
"did you mean". A prefix search would turn the column into a browsable directory
of everyone with a meeting in the building — type "a" and read the lobby.

**No browse-all endpoint for people.** `/api/rooms` lists rooms, which are not
personal data. There is no endpoint that lists meetings or attendees across the
building.

**Names are POSTed, not in the query string.** A name in a URL ends up in
access logs, browser history and referrer headers.

**Subjects are never rendered.** Meeting titles leak more than names do
("Exitgesprek J. Nap"). The store keeps `subject` for debugging, but no endpoint
returns it and no component displays it.

**Attendees are not hydrated on read.** `QueryMeetingsAsync` leaves
`Attendees` empty, so the list cannot leak into a response by accident.

**A failed lookup is indistinguishable from an unknown name.** Both return an
empty result set, so the kiosk does not confirm whether a given person exists in
the tenant.

**Short retention.** `Storage:RetainPastMeetingsFor` defaults to 2 hours, and
the sync replaces the whole window each cycle. There is no reason for a machine
in a lobby to hold yesterday's attendee lists.

**Idle reset and incognito.** The UI returns to the attract screen after 45s of
no touch, and Chromium runs `--incognito`. Without both, the last visitor's name
and meeting sit on the screen until someone else walks up.

**Minimal field selection.** The Graph query `$select`s only what is used, so
the app does not pull body, location, organiser or response status over the wire
at all.

## Decisions still to make

- **Signage.** Whether a notice at the column is required, and in what wording,
  is a question for whoever owns the building's privacy statement.
- **Searches are not logged** today. Keep it that way; if a diagnostic log is
  ever added, log the *outcome* (hit/miss) and not the name.
- **Scope of the Entra app.** See [outlook-setup.md](outlook-setup.md): without
  an `ApplicationAccessPolicy`, the credential on this device can read every
  mailbox in the tenant. That is the single largest risk in the whole system.
