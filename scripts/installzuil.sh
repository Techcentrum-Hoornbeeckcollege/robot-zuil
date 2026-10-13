#!/usr/bin/env bash

set -euo pipefail

SRC="${1:?usage: install.sh <path-to-build-output>}"
DEST="/opt/zuil"
DEPLOY="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ "$EUID" -ne 0 ]; then
  echo "Run as root." >&2
  exit 1
fi

echo "==> Creating the zuil user"
id -u zuil >/dev/null 2>&1 || useradd --system --create-home --shell /usr/sbin/nologin zuil

echo "==> Granting hardware access"
# Only add user to groups that actually exist on this system (prevents usermod failure)
AVAILABLE_GROUPS=""
for grp in i2c gpio video; do
  if getent group "$grp" >/dev/null 2>&1; then
    AVAILABLE_GROUPS="${AVAILABLE_GROUPS:+$AVAILABLE_GROUPS,}$grp"
  fi
done
if [ -n "$AVAILABLE_GROUPS" ]; then
  usermod -aG "$AVAILABLE_GROUPS" zuil
  echo "    added zuil to groups: $AVAILABLE_GROUPS"
else
  echo "    no hardware groups (i2c, gpio, video) found on this system; skipping."
fi

echo "==> Enabling the i2c bus"
if ! grep -q '^dtparam=i2c_arm=on' /boot/firmware/config.txt 2>/dev/null; then
  echo 'dtparam=i2c_arm=on' >> /boot/firmware/config.txt
  echo "    added dtparam=i2c_arm=on (reboot required)"
fi

# Generate a unique release ID using timestamp (or git commit / version)
RELEASE_ID="$(date +%Y%m%d_%H%M%S)"
RELEASE_DIR="$DEST/releases/$RELEASE_ID"

echo "==> Staging new release to $RELEASE_DIR"
# config/ and data/ live OUTSIDE the release directory on purpose: a release is
# disposable, and everything the operator sets up has to outlive it.
mkdir -p "$RELEASE_DIR" /var/cache/zuil/extract "$DEST/config" "$DEST/data"
cp -r "$SRC/." "$RELEASE_DIR/"
chmod +x "$RELEASE_DIR/zuil"
# Ensure all shell/executable scripts in release and scripts folder have execute permissions
find "$RELEASE_DIR" -type f \( -name "*.sh" -o -name "*.py" -o -executable -o ! -name "*.*" \) -exec chmod +x {} +

echo "==> Updating active symlink"
ln -sfn "$RELEASE_DIR" "$DEST/current"
echo "$RELEASE_ID" > "$DEST/version"

echo "==> Seeding the persistent config directory"
# Seed only what is missing. An update must never overwrite these: the room list
# and the production settings are the operator's, not the release's.
if [ ! -f "$DEST/config/appsettings.Production.json" ] \
   && [ -f "$RELEASE_DIR/config/appsettings.Production.json.example" ]; then
  cp "$RELEASE_DIR/config/appsettings.Production.json.example" \
     "$DEST/config/appsettings.Production.json"
  echo "    created appsettings.Production.json"
fi

# rooms.json is read once, on the very first start, to populate the room table.
# It has to be in the persistent directory, because that is the one the app
# reads (ZUIL_CONFIG_DIR, below).
if [ ! -f "$DEST/config/rooms.json" ] && [ -f "$RELEASE_DIR/config/rooms.json" ]; then
  cp "$RELEASE_DIR/config/rooms.json" "$DEST/config/"
  echo "    created rooms.json"
fi

# The environment the service and runzuil.sh both read. Written once and then
# left alone, so an update never regenerates the admin password.
#
# ZUIL_CONFIG_DIR is the important one: without it the app reads
# $RELEASE_DIR/config, which this script does not write and the next release
# replaces — so every setting would silently revert on update.
if [ ! -f "$DEST/config/.env" ]; then
  GENERATED_PASSWORD="$(tr -dc 'A-Za-z0-9' </dev/urandom | head -c 20)"
  cat > "$DEST/config/.env" <<EOF
# Written by scripts/installzuil.sh on $(date -Is). Safe to edit; not overwritten.
ZUIL_CONFIG_DIR=$DEST/config
ZUIL_Storage__DatabasePath=$DEST/data/zuil-cache.db
ZUIL_Admin__Password=$GENERATED_PASSWORD
EOF
  echo "    created .env with a generated admin password"
fi

echo "==> Setting secure permissions"
chown -R zuil:zuil "$DEST" /var/cache/zuil
# Both hold credentials: a certificate password in one, the admin password in
# the other. The database and secret.key under data/ are written by the service.
for f in "$DEST/config/appsettings.Production.json" "$DEST/config/.env"; do
  if [ -f "$f" ]; then
    chmod 600 "$f"
  fi
done

echo "==> Installing systemd units"
# Assuming unit files are in the repository's deploy/ folder alongside our script
if [ -d "$DEPLOY/../deploy" ]; then
  cp "$DEPLOY/../deploy/zuil.service" "$DEPLOY/../deploy/zuil-kiosk.service" /etc/systemd/system/
elif [ -f "$DEPLOY/zuil.service" ]; then
  cp "$DEPLOY/zuil.service" "$DEPLOY/zuil-kiosk.service" /etc/systemd/system/
fi

systemctl daemon-reload
systemctl enable zuil.service zuil-kiosk.service

echo
echo "Done! Installation/Update successful."
echo "Start with:  systemctl start zuil zuil-kiosk"
echo "Logs:        journalctl -u zuil -f"
echo

if [ -n "${GENERATED_PASSWORD:-}" ]; then
  echo "Admin password:  $GENERATED_PASSWORD"
  echo "                 Stored in $DEST/config/.env. This is the only time it is"
  echo "                 printed, so write it down now."
  echo
  echo "Then, at the kiosk itself:"
  echo "  1. Hold the bottom-right corner of the screen for three seconds."
  echo "  2. Log in and fill in Outlook: tenant, client id and client secret."
  echo "  3. Check the rooms under Zalen."
  echo "  See docs/admin.md."
  echo
fi

echo "Back up $DEST/data/zuil-cache.db and $DEST/data/secret.key together:"
echo "either one alone cannot recover the Outlook credential."
