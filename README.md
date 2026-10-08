# robot-zuil

A wayfinding kiosk for a building lobby. A visitor enters their name on a
touchscreen; the kiosk looks them up across six Outlook room calendars, tells
them which room their meeting is in and how to walk there, and an ESP32 lights
the matching indicator on the column.

It also shows what is on: **Straks**, every upcoming meeting across the building
in order, and **Alle agenda's**, one column per room. Meeting subjects are hidden
by default and the server is what hides them — see [docs/privacy.md](docs/privacy.md).

Everything configurable is configured **from the touchscreen**: Outlook
credentials, the room list and their walking directions, sync timing, device
parameters, privacy and display. Hold the bottom-right corner for three seconds.
See [docs/admin.md](docs/admin.md). Only what cannot change without a restart
stays in a file.

Ships as **one self-contained binary** — frontend, backend and hardware driver
in a single file.

## Stack

| Piece | Choice |
|---|---|
| Backend | ASP.NET Core 9, minimal APIs, single-file self-contained publish |
| Frontend | Vite + React + TypeScript, embedded in the binary as resources |
| Outlook | MSAL for app-only tokens + Graph `/calendarView` over `HttpClient` |
| Cache | SQLite (`Microsoft.Data.Sqlite`), WAL mode |
| Settings | SQLite, edited from the GUI; the Graph secret encrypted with AES-GCM |
| Hardware | `System.Device.Gpio` → i2c to an ESP32 at 100 kHz |
| Kiosk | systemd + Chromium `--kiosk` on the Pi |

## Layout

```
build/          build.sh (single binary) and dev.sh (laptop dev loop)
config/         first-run room seed + the few file-only settings — ships BESIDE the binary
deploy/         systemd units and install.sh for the Pi
docs/           architecture, Outlook setup, wiring, deployment, privacy, admin runbook
firmware/esp32/ PlatformIO i2c-slave firmware
frontend/       React SPA: shell + features/{search,agenda,admin,settings}
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

## How to Run & Install

On your Raspberry Pi target device:

### 1. Download and extract the latest release

```bash
mkdir -p /tmp/zuil-setup && cd /tmp/zuil-setup

# Download latest release asset
curl -sL https://github.com/Techcentrum-Hoornbeeckcollege/robot-zuil/releases/latest/download/zuil-linux-arm64.zip -o zuil-linux-arm64.zip

# Unzip package
unzip -o zuil-linux-arm64.zip
```

### 2. Configure production settings

Before starting the service, populate your Entra ID tenant and client credentials in the production config:

```bash
sudo nano /opt/zuil/config/appsettings.Production.json
```
*(Fill in your `TenantId`, `ClientId`, and certificate path/secret as detailed in [docs/outlook-setup.md](docs/outlook-setup.md)).*

### 3. Run the installer / updater

Run the unified idempotent installer/updater as root, pointing to the extracted `linux-arm64` directory:

```bash
sudo ./scripts/installzuil.sh ./linux-arm64
```

This installer automatically:
- Creates the dedicated `zuil` system user and grants hardware group permissions (`i2c`, `gpio`, `video`).
- Enables the I²C bus in `/boot/firmware/config.txt`.
- Stages the build into an immutable release directory (`/opt/zuil/releases/<timestamp>/`) and atomically updates the active symlink (`/opt/zuil/current`).
- Handles production configuration templates safely without overwriting existing credentials.
- Registers and enables the systemd services (`zuil.service` and `zuil-kiosk.service`).

To start or check the services manually:
```bash
sudo systemctl start zuil zuil-kiosk
sudo journalctl -u zuil -f
```

## Build the deliverable

```bash
./build/build.sh linux-arm64     # Raspberry Pi
./build/build.sh linux-x64       # x86_64 Ubuntu desktop
```

Produces `artifacts/<rid>/zuil`. See [docs/deploy.md](docs/deploy.md).

## Before this runs for real

1. **Change the admin password.** It ships as `12345678`, the kiosk's own browser
   can reach `/api/admin`, and the touchscreen is the keyboard — so until it is
   changed, anyone who has read this repository can open the settings panel from
   the lobby. `deploy/install.sh` generates a random one into
   `/etc/zuil/zuil.env` and prints it once.
2. **[docs/outlook-setup.md](docs/outlook-setup.md)** — register the Entra app,
   and *scope it to the six mailboxes with an `ApplicationAccessPolicy`*.
   Without that step, the credential on a device in a public lobby can read every
   calendar in the tenant. This matters more now that the credential is typed in
   on a touchscreen rather than placed on the device by whoever set it up.
3. **[docs/privacy.md](docs/privacy.md)** — read before widening anything the
   kiosk shows. Several decisions (exact-match only, no autocomplete, subjects
   hidden by default and nulled server-side) are there for GDPR reasons and are
   expensive to retrofit. Turning subjects on for *Iedereen* is a decision for
   whoever owns the building's privacy statement.
4. **[docs/wiring.md](docs/wiring.md)** — share a ground, keep i2c short, leave
   the bus at 100 kHz.
5. Make sure NTP works on the Pi. It has no RTC, and a wayfinding kiosk with a
   wrong clock confidently sends people to the wrong room.
6. **Back up `zuil-cache.db` and `secret.key` together.** The database holds
   every setting and the encrypted Outlook credential; the key file is what
   decrypts it. Either one alone recovers nothing.

## Known open decision

What the ESP32 physically drives is not settled, so `SHOW_DIRECTION` currently
carries a single `signalId` and the firmware blinks the onboard LED. An LED
strip, a bank of lit signs and a moving arrow each want a different payload —
see the end of [schema/device-protocol.md](schema/device-protocol.md).
