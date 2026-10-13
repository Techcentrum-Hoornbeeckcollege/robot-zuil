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
# Only the release goes here. There is nothing to seed and nothing to preserve:
# the kiosk keeps everything in /var/lib/zuil, which systemd creates and owns
# (StateDirectory in zuil.service), so a release directory is fully disposable.
mkdir -p "$RELEASE_DIR" /var/cache/zuil/extract
cp -r "$SRC/." "$RELEASE_DIR/"
chmod +x "$RELEASE_DIR/zuil"
# Ensure all shell/executable scripts in release and scripts folder have execute permissions
find "$RELEASE_DIR" -type f \( -name "*.sh" -o -name "*.py" -o -executable -o ! -name "*.*" \) -exec chmod +x {} +

echo "==> Updating active symlink"
ln -sfn "$RELEASE_DIR" "$DEST/current"
echo "$RELEASE_ID" > "$DEST/version"

echo "==> Setting secure permissions"
chown -R zuil:zuil "$DEST" /var/cache/zuil

echo "==> Installing systemd units"
# Copy directly from the repository deploy/ folder or fallback to local dir
if [ -d "$DEPLOY/../deploy" ]; then
  cp "$DEPLOY/../deploy/zuil.service" "$DEPLOY/../deploy/zuil-kiosk.service" /etc/systemd/system/
elif [ -d "$DEPLOY/deploy" ]; then
  cp "$DEPLOY/deploy/zuil.service" "$DEPLOY/deploy/zuil-kiosk.service" /etc/systemd/system/
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
echo "Then, at the kiosk itself - nothing else needs editing over SSH:"
echo "  1. Tap Instellingen at the bottom of the screen."
echo "  2. Log in with  admin / password"
echo "  3. Change those under Account. Until you do, anyone who knows this"
echo "     project can open the settings panel from the lobby."
echo "  4. Fill in Outlook: tenant, client id and client secret."
echo "  See docs/admin.md."
echo
echo "Everything is kept in /var/lib/zuil. Back up zuil-cache.db and secret.key"
echo "together: either one alone cannot recover the Outlook credential."
