# Deploying to the Pi

## Build

On a machine with the .NET SDK and Node:

```bash
./build/build.sh linux-arm64
```

Output: `artifacts/linux-arm64/zuil` — one self-contained binary with the React
frontend embedded, and nothing beside it. There is no configuration directory to
ship: the kiosk is set up from its own screen.

Cross-building for arm64 from an x86_64 machine works fine; .NET does not need a
matching host architecture. (This is one of the reasons the Python sidecar idea
was dropped: PyInstaller cannot cross-compile, .NET can.)

For an x86_64 Ubuntu desktop instead:

```bash
./build/build.sh linux-x64
```

## Install

Publishing a GitHub release runs `.github/workflows/main.yml`, which builds for
linux-arm64 and attaches `zuil-linux-arm64.zip`. Copy that to the device and run
the installer:

```bash
scp zuil-linux-arm64.zip zuil-pi:/tmp/
ssh zuil-pi 'cd /tmp && unzip -o zuil-linux-arm64.zip -d zuil-release \
  && sudo ./zuil-release/scripts/installzuil.sh ./zuil-release/linux-arm64'
```

That installs system dependencies (ICU for .NET globalization, curl for health
checks), creates the `zuil` user, grants i2c/gpio access, enables the bus,
installs the application and update systemd units, and **generates a random
admin password, printing it once**.
Write it down.

```bash
sudo systemctl start zuil zuil-kiosk
```

## Automatic updates

`zuil-update.timer` runs every day at 17:00 local time. It downloads the latest
GitHub release, verifies the `SHA256SUMS` asset, and never changes
`/var/lib/zuil`.

The updater reuses the installer’s release directories and symlink. It only
switches the active release after the archive is verified. After restarting
the backend it waits for `/api/health`; if the health check does not succeed,
the previous release is restored and both services are restarted.

To trigger or inspect it manually:

```bash
sudo systemctl start zuil-update.service
sudo journalctl -u zuil-update.service
systemctl list-timers zuil-update.timer
```

### What goes where

One file, one directory:

```
/opt/zuil/
  releases/20261013_104500/zuil     each build, one file per release
  releases/20261013_161200/zuil
  current -> releases/20261013_161200    what the service runs

/var/lib/zuil/                      everything the operator set up
  zuil-cache.db                     settings, rooms, credentials, the encrypted secret
  secret.key                        what decrypts it
```

There is no config directory, no appsettings.json and no environment file. The
release directory is fully disposable; `/var/lib/zuil` is created and owned by
systemd (`StateDirectory=zuil` in the unit) and is never touched by an update.

The backend logs where it landed on every start:

```
Database /var/lib/zuil/zuil-cache.db, listening on http://127.0.0.1:5000
```

### Rolling back

```bash
sudo ln -sfn /opt/zuil/releases/<older-id> /opt/zuil/current
sudo systemctl restart zuil
```

## First run, at the kiosk itself

Nothing else needs editing over SSH. The kiosk starts with no Outlook
credentials, says so, and waits — it does not fail in a loop.

1. Tap **Instellingen** at the bottom of the screen.
2. Log in with **`admin` / `password`**.
3. **Account** — change both, now. Until you do, anyone who knows this project
   can reconfigure the kiosk from the lobby.
4. **Outlook** — tenant id, client id, client secret, then *Opslaan en testen*.
5. **Zalen** — check the six example rooms, and the indicator numbers against
   the physical build.

The first sync happens within a second or two of saving the credentials, not at
the next interval. See [admin.md](admin.md) for the rest of the panel.

If this device previously ran a build that kept its database under
`/opt/zuil/data`, that database is copied to `/var/lib/zuil` on the first start,
so the rooms, the settings and the Outlook credential all survive. The old copy
is left where it is in case you roll back; delete it once this release is known
good, since it holds a credential.

## Updating

Run the same installer again with the new build. It stages a new release
directory and repoints `current`; it never touches `/var/lib/zuil`:

```bash
sudo ./zuil-release/scripts/installzuil.sh ./zuil-release/linux-arm64
sudo systemctl restart zuil zuil-kiosk
```

Schema changes are applied automatically at startup and are written to be
idempotent, so there is no migration step.

## Backups

```bash
sudo systemctl stop zuil
sudo tar czf zuil-backup.tar.gz -C /var/lib zuil
sudo systemctl start zuil
```

**`zuil-cache.db` and `secret.key` together.** Either one alone cannot recover
the Outlook credential. Treat the result as sensitive: the database holds that
credential, the admin password hash, and recent attendee names.

If `secret.key` is lost, the kiosk reports Outlook as not configured and waits
for someone to re-enter the secret. It does not crash.

## Checks

```bash
journalctl -u zuil -f                       # backend log
curl -s localhost:5000/api/health | jq      # status, sync, device, clock
i2cdetect -y 1                              # is the ESP32 answering at 0x42
timedatectl show -p NTPSynchronized --value  # must be "yes"
```

## Things that will actually go wrong

**The clock.** A Pi has no battery-backed RTC. Boot it without a network and it
believes it is whenever it last shut down — and an app whose entire job is
"which meeting is on now" will point visitors at the wrong room with total
confidence. The app refuses to sync until `timedatectl` reports NTP
synchronisation, and `/api/health` exposes `clockSynchronized`. If the building
blocks outbound NTP, either allow it or fit an RTC module.

**Chromium memory over weeks of uptime.** It is a separate systemd unit
precisely so it can be restarted without dropping the backend. If the screen
degrades after a few weeks, add a nightly `systemctl restart zuil-kiosk` timer.

**Certificate expiry.** See [outlook-setup.md](outlook-setup.md). The failure
mode is silent: a stale cache behind a staleness banner nobody reads.

**Screen blanking.** Handled by the `xset` calls in `zuil-kiosk.service`. If the
screen still blanks, the compositor is probably Wayland rather than X11, and
those calls do nothing — use `cage` or configure `wlr-randr` instead.

## Trimming

`PublishTrimmed` is off. Turn it on only once everything works, and then test
the whole app hard — MSAL and SQLite both use reflection, and the failure mode
is a runtime exception on a kiosk bolted to a wall rather than a build error.
