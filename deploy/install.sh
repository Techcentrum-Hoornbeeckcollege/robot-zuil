#!/usr/bin/env bash
#
# One-time device setup. Run on the Pi as root, from a directory containing the
# build output (artifacts/<rid>/).
#
#   sudo ./install.sh /path/to/artifacts/linux-arm64

set -euo pipefail

SRC="${1:?usage: install.sh <path-to-build-output>}"
DEST=/opt/zuil
DEPLOY="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ "$EUID" -ne 0 ]; then
  echo "Run as root." >&2
  exit 1
fi

echo "==> Creating the zuil user"
id -u zuil >/dev/null 2>&1 || useradd --system --create-home --shell /usr/sbin/nologin zuil

echo "==> Granting hardware access"
# Group membership instead of running the service as root.
usermod -aG i2c,gpio,video zuil

echo "==> Enabling the i2c bus"
# On Raspberry Pi OS raspi-config does this; on Ubuntu the overlay goes in
# /boot/firmware/config.txt. Either way a reboot is needed for /dev/i2c-1.
if ! grep -q '^dtparam=i2c_arm=on' /boot/firmware/config.txt 2>/dev/null; then
  echo 'dtparam=i2c_arm=on' >> /boot/firmware/config.txt
  echo "    added dtparam=i2c_arm=on (reboot required)"
fi

echo "==> Installing to $DEST"
mkdir -p "$DEST" /var/cache/zuil/extract
cp -r "$SRC/." "$DEST/"
chmod +x "$DEST/zuil"
chown -R zuil:zuil "$DEST" /var/cache/zuil

if [ ! -f "$DEST/config/appsettings.Production.json" ]; then
  cp "$DEST/config/appsettings.Production.json.example" \
     "$DEST/config/appsettings.Production.json"
fi
# The file may hold a certificate password; keep it off other accounts.
chmod 600 "$DEST/config/appsettings.Production.json"
chown zuil:zuil "$DEST/config/appsettings.Production.json"

echo "==> Setting an admin password"
# In an EnvironmentFile rather than the unit, because `systemctl cat` is
# world-readable and this file is 0600. The shipped default is 12345678, which
# anyone who has read this project knows, and the kiosk's own browser can reach
# /api/admin — so a generated password is the difference between a locked
# settings panel and an open one.
mkdir -p /etc/zuil
if [ ! -f /etc/zuil/zuil.env ]; then
  ADMIN_PASSWORD="$(tr -dc 'A-Za-z0-9' </dev/urandom | head -c 20)"
  printf 'ZUIL_Admin__Password=%s\n' "$ADMIN_PASSWORD" > /etc/zuil/zuil.env
  GENERATED_PASSWORD="$ADMIN_PASSWORD"
fi
chmod 600 /etc/zuil/zuil.env
chown root:root /etc/zuil/zuil.env

echo "==> Checking the clock"
# A Pi has no RTC. If NTP never syncs, a wayfinding kiosk shows the wrong
# meetings with total confidence, so make sure time sync is on.
timedatectl set-ntp true || true
timedatectl show --property=NTPSynchronized --value

echo "==> Installing systemd units"
cp "$DEPLOY/zuil.service" "$DEPLOY/zuil-kiosk.service" /etc/systemd/system/
systemctl daemon-reload
systemctl enable zuil.service zuil-kiosk.service

echo
echo "Done. Start with:  systemctl start zuil zuil-kiosk"
echo "Logs:              journalctl -u zuil -f"
echo

if [ -n "${GENERATED_PASSWORD:-}" ]; then
  echo "Admin password:    $GENERATED_PASSWORD"
  echo "                   Written to /etc/zuil/zuil.env. This is the only time"
  echo "                   it is printed, so write it down now."
  echo
fi

echo "Then, at the kiosk itself:"
echo "  1. Hold the bottom-right corner of the screen for three seconds."
echo "  2. Log in and fill in Outlook: tenant, client id and client secret."
echo "  3. Check the rooms under Zalen."
echo "See docs/admin.md. Back up $DEST/zuil-cache.db and $DEST/secret.key together:"
echo "either one alone cannot recover the Outlook credential."
