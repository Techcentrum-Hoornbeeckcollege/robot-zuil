# robot-zuil

A wayfinding kiosk for a building lobby. A visitor enters their name on a
touchscreen; the kiosk looks them up across six Outlook room calendars, tells
them which room their meeting is in and how to walk there, and an ESP32 lights
the matching indicator on the column.

Ships as **one self-contained binary** — frontend, backend and hardware driver
in a single file.

## Stack

| Piece | Choice |
|---|---|
| Backend | ASP.NET Core 9, minimal APIs, single-file self-contained publish |
| Frontend | Vite + React + TypeScript, embedded in the binary as resources |
| Outlook | MSAL for app-only tokens + Graph `/calendarView` over `HttpClient` |
| Cache | SQLite (`Microsoft.Data.Sqlite`), WAL mode |
| Hardware | `System.Device.Gpio` → i2c to an ESP32 at 100 kHz |
| Kiosk | systemd + Chromium `--kiosk` on the Pi |

## Layout

```
build/          build.sh (single binary) and dev.sh (laptop dev loop)
config/         rooms.json + production settings — ships BESIDE the binary
deploy/         systemd units and install.sh for the Pi
docs/           architecture, Outlook setup, wiring, deployment, privacy
firmware/esp32/ PlatformIO i2c-slave firmware
frontend/       React SPA
schema/         the Pi ↔ ESP32 protocol contract, shared by C# and C++
src/Zuil.Core/      models + interfaces, zero I/O
src/Zuil.Graph/     ICalendarSource over Microsoft Graph (+ a fake)
src/Zuil.Storage/   IMeetingStore over SQLite
src/Zuil.Device/    IDeviceBridge over i2c (+ a fake)
src/Zuil.Host/      the executable
tests/
```

## Develop

No Entra tenant, no Pi and no ESP32 needed:

```bash
./build/dev.sh
```

Backend with fakes on `:5000`, Vite with HMR on `:5173`. Open
<http://localhost:5173>.

## Build the deliverable

```bash
./build/build.sh linux-arm64     # Raspberry Pi
./build/build.sh linux-x64       # x86_64 Ubuntu desktop
```

Produces `artifacts/<rid>/zuil`. See [docs/deploy.md](docs/deploy.md).

## Before this runs for real

1. **[docs/outlook-setup.md](docs/outlook-setup.md)** — register the Entra app,
   and *scope it to the six mailboxes with an `ApplicationAccessPolicy`*.
   Without that step, the credential on a device in a public lobby can read every
   calendar in the tenant.
2. **[docs/privacy.md](docs/privacy.md)** — read before changing the search UI.
   Several decisions (exact-match only, no autocomplete, no subjects on screen)
   are there for GDPR reasons and are expensive to retrofit.
3. **[docs/wiring.md](docs/wiring.md)** — share a ground, keep i2c short, leave
   the bus at 100 kHz.
4. Make sure NTP works on the Pi. It has no RTC, and a wayfinding kiosk with a
   wrong clock confidently sends people to the wrong room.

## Known open decision

What the ESP32 physically drives is not settled, so `SHOW_DIRECTION` currently
carries a single `signalId` and the firmware blinks the onboard LED. An LED
strip, a bank of lit signs and a moving arrow each want a different payload —
see the end of [schema/device-protocol.md](schema/device-protocol.md).
