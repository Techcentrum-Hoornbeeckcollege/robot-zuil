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

## Data flow

1. `CalendarSyncWorker` wakes every `Sync:Interval`.
2. It reads `/calendarView` for each of the six room mailboxes over a window of
   `now - 2h` to `now + 36h`.
3. It replaces the SQLite cache in **one transaction**.
4. It pushes `cacheUpdated` over SignalR.

Visitor requests only ever read SQLite. Nothing visitor-facing calls Graph
inline. That is deliberate: a lobby screen must not go blank because a token
refresh is slow or the building's uplink is down.

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

**Transport behind `IDeviceBridge`.** ESP32 i2c-slave support is the fragile
part of this design (see [device-protocol.md](../schema/device-protocol.md)). If
it misbehaves, a UART implementation drops in without touching anything else.
