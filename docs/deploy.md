# Deploying to the Pi

## Build

On a machine with the .NET SDK and Node:

```bash
./build/build.sh linux-arm64
```

Output: `artifacts/linux-arm64/zuil` — one self-contained binary with the React
frontend embedded — plus the `config/` directory it reads at startup.

Cross-building for arm64 from an x86_64 machine works fine; .NET does not need a
matching host architecture. (This is one of the reasons the Python sidecar idea
was dropped: PyInstaller cannot cross-compile, .NET can.)

For an x86_64 Ubuntu desktop instead:

```bash
./build/build.sh linux-x64
```

## Install

Copy the output to the device and run the installer once:

```bash
scp -r artifacts/linux-arm64 zuil-pi:/tmp/zuil-build
scp -r deploy zuil-pi:/tmp/zuil-deploy
ssh zuil-pi 'sudo /tmp/zuil-deploy/install.sh /tmp/zuil-build'
```

That creates the `zuil` user, grants i2c/gpio access, enables the bus, installs
to `/opt/zuil`, and sets up both systemd units.

Then fill in the credentials and start:

```bash
sudo nano /opt/zuil/config/appsettings.Production.json
sudo systemctl start zuil zuil-kiosk
```

## Updating

```bash
sudo systemctl stop zuil zuil-kiosk
sudo cp -r /tmp/zuil-build/zuil /opt/zuil/zuil
sudo systemctl start zuil zuil-kiosk
```

`config/` is left alone on purpose, so an update never clobbers the room
directions or the certificate.

## Checks

```bash
journalctl -u zuil -f                       # backend log
curl -s localhost:5000/api/health | jq      # sync + device + clock state
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
