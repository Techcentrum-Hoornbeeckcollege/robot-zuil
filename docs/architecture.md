# Architecture

## What it does

A standing kiosk in a building lobby. A visitor enters their name, the kiosk
tells them which of six meeting rooms to walk to, and an ESP32 lights the
matching physical indicator on the column.

## Shape

```
   ┌─────────────── Raspberry Pi (Ubuntu / Pi OS, arm64) ────────────────┐
   │                                                                     │
   │  Chromium --kiosk ──HTTP/WS──▶  zuil  (single self-contained file)   │
   │  (zuil-kiosk.service)           (zuil.service)                      │
   │                                   │                                 │
   │                 React SPA  ◀──────┤ embedded in the binary          │
   │                                   │                                 │
   │                    SQLite cache ◀─┤ CalendarSyncWorker              │
   │                    + settings     │                                 │
   │                    + rooms      ◀─┤ admin API                       │
   │                    + secret       │                                 │
   │                                   │                                 │
   │                     secret.key  ◀─┤ AES-GCM, beside the database    │
   │                                   │                                 │
   └───────────────────────────────────┼─────────────────────────────────┘
                                       │                     │
                        i2c @100kHz + attention GPIO    HTTPS
                                       │                     │
                                 ┌─────▼─────┐      ┌────────▼────────┐
                                 │  ESP32    │      │ Microsoft Graph │
                                 │ indicators│      │ 6 room mailboxes│
                                 └───────────┘      └─────────────────┘
```

## Projects

| Project | Role | May reference |
|---|---|---|
| `Zuil.Core` | Models and interfaces. No I/O at all. | nothing |
| `Zuil.Graph` | `ICalendarSource` over Graph, plus a fake | Core |
| `Zuil.Storage` | `IMeetingStore` over SQLite | Core |
| `Zuil.Device` | `IDeviceBridge` over i2c, plus a fake | Core |
| `Zuil.Host` | The executable: endpoints, sync worker, static files | all |

`Zuil.Core` having no I/O dependency is the load-bearing rule here. It is what
lets the Graph, storage and device layers each be swapped or faked
independently — and the fakes are what make the kiosk developable on a laptop.

## Configuration

Everything an administrator can reasonably change lives in the database and is
edited from the touchscreen. `KioskSettings` is one immutable snapshot; the
`SettingsProvider` hands it out and raises `Changed` after a reload.

```
admin PUT ─▶ SqliteSettingsStore ─▶ SettingsProvider.ReloadAsync
                                         │
                      ┌──────────────────┼──────────────────┐
                      ▼                  ▼                  ▼
            SqliteBackedRoomDirectory   GraphTokenProvider  CalendarSyncWorker
            (swaps its snapshot)        (rebuilds MSAL)     (wakes, resyncs)
                                             │
                                        hub: settingsUpdated ─▶ open kiosk pages
```

**Why not an `IConfigurationSource` over SQLite**, which would have kept the
existing `IOptions` plumbing: that plumbing never live-reloaded here. Every
consumer captured `options.Value` in its constructor, so a reload token firing
changed nothing until each one was rewritten. Given the call sites had to change
either way, a typed snapshot with real validation beat a string-keyed dictionary
with none.

Reading a setting once per operation is deliberate. The sync worker takes one
snapshot per pass, so an edit halfway through cannot mix two room lists into one
cache swap.

Three device settings — bus, address, attention pin — are still read only at
startup, because re-opening an `I2cDevice` and re-registering GPIO callbacks on a
running kiosk is a new failure mode for near-zero benefit. The admin panel badges
them.

What stays in a file: the admin password, the Kestrel URL, the database path, the
Chromium path, and a certificate path. See [config/README.md](../config/README.md).

## Data flow

1. `CalendarSyncWorker` wakes every `Sync:Interval`, or immediately when settings
   change or an administrator asks for a sync.
2. It reads `/calendarView` for each of the six room mailboxes over a window of
   `now - 2h` to `now + 36h`.
3. It replaces the SQLite cache in **one transaction**.
4. It pushes `cacheUpdated` over SignalR.

Visitor requests only ever read SQLite. Nothing visitor-facing calls Graph
inline. That is deliberate: a lobby screen must not go blank because a token
refresh is slow or the building's uplink is down.

An unconfigured kiosk is a state, not a failure: with no credentials the worker
waits on the settings-changed signal rather than retrying on a timer, so it does
not log an error every three minutes for the life of the device.

## The agenda surface

Two reads across all rooms — `GetUpcomingAsync` and `GetWindowAsync` — feed
*Straks* and *Alle agenda's*. Neither takes a privacy flag: the store returns the
whole `Meeting`, and `Zuil.Core.Agenda.AgendaProjection` is the only thing that
decides what may be published, by nulling the subject rather than trusting the
client not to render it. One function, one test, no endpoint that can forget.
See [privacy.md](privacy.md#the-agenda-views).

## The admin surface

Two route groups: `/api/admin/session` is public, because logging in cannot
require a session, and everything else sits behind `AdminSessionFilter`. A filter
attached to the group rather than middleware, so it is visible in the route table
instead of depending on ordering.

Sessions are opaque random tokens, hashed in process, in an `HttpOnly` cookie.
Not cookie authentication: that needs a Data Protection key ring, and
`ProtectHome=true` in the unit makes its default key home unwritable — it would
fall back to ephemeral keys and sign everyone out on every restart anyway. The
same constraint is why the Graph secret uses AES-GCM against an explicit key file
rather than Data Protection.

`GraphTokenProvider` caches its MSAL client keyed by a fingerprint of the
credentials that built it. A changed fingerprint drops the client *and its token
cache*, which is the point: a token minted with a since-revoked secret must not
outlive the revocation.

## Why these choices

**No `Microsoft.Graph` SDK.** It is large and reflection-heavy, which fights
single-file publish, and the app needs exactly one endpoint. MSAL for the token
plus `HttpClient` and a source-generated serializer is smaller and more direct.

**`/calendarView`, not `/events`.** calendarView expands a recurring series into
concrete occurrences inside the window. `/events` returns the series master and
a recurrence rule, leaving you to expand it — including exceptions and
cancellations — which is a well-known source of wrong-room bugs.

**Polling, not Graph change notifications.** Webhooks need a publicly reachable
HTTPS endpoint. A kiosk on a building LAN does not have one. Six requests every
few minutes is nothing.

**Cache-first reads.** See above: resilience, and a hard upper bound on how much
Graph traffic a touchscreen can generate.

**One SQLite connection, one write lock.** `ZuilDatabase` owns both and the two
stores share them. A second connection for settings would reintroduce exactly the
`SQLITE_BUSY` contention the single-connection arrangement exists to avoid.

**No router in the SPA.** Chromium runs with `--app=` and
`--overscroll-history-navigation=0`: no address bar, no back button, no gestures.
A deep link is a link nobody can follow, and an admin URL in history is a hint
worth not leaving around. Views are a discriminated union with an exhaustive
switch, so adding one fails the build until it is rendered.

**Transport behind `IDeviceBridge`.** ESP32 i2c-slave support is the fragile
part of this design (see [device-protocol.md](../schema/device-protocol.md)). If
it misbehaves, a UART implementation drops in without touching anything else.
