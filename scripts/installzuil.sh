#!/usr/bin/env bash

set -euo pipefail

SRC="${1:?usage: installzuil.sh <path-to-build-output>}"
DEST="/opt/zuil"
DEPLOY="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ "$EUID" -ne 0 ]; then
  echo "Run as root." >&2
  exit 1
fi

echo "==> Installing system dependencies"
# The .NET self-contained binary needs ICU on the host for globalization
# (InvariantGlobalization is false — see Directory.Build.props). curl is
# needed by zuil-kiosk.service's ExecStartPre health check.
if [ "${ZUIL_SKIP_DEPENDENCIES:-0}" = "1" ]; then
  echo "    dependency installation skipped"
elif command -v apt-get >/dev/null 2>&1; then
  apt-get update -qq
  # ICU package name varies by distro version (libicu70, libicu72, libicu74 …)
  ICU_PKG="$(apt-cache search '^libicu[0-9]' 2>/dev/null | grep -v -- '-dev' | head -1 | cut -d' ' -f1)"
  if [ -n "$ICU_PKG" ]; then
    apt-get install -y --no-install-recommends "$ICU_PKG" curl unzip
    echo "    installed $ICU_PKG, curl and unzip"
  else
    echo "    WARNING: Could not find ICU package; install libicu manually." >&2
  fi
elif command -v dnf >/dev/null 2>&1; then
  dnf install -y libicu curl unzip
  echo "    installed libicu, curl and unzip"
elif command -v yum >/dev/null 2>&1; then
  yum install -y libicu curl unzip
  echo "    installed libicu, curl and unzip"
else
  echo "    WARNING: No supported package manager found (apt/dnf/yum)."
  echo "    Install libicu and curl manually before starting the service." >&2
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
# /boot/firmware/config.txt is a Raspberry Pi file. On systems where it does
# not exist (e.g. a standard Ubuntu VM), skip gracefully rather than failing.
if [ -f /boot/firmware/config.txt ]; then
  if ! grep -q '^dtparam=i2c_arm=on' /boot/firmware/config.txt; then
    echo 'dtparam=i2c_arm=on' >> /boot/firmware/config.txt
    echo "    added dtparam=i2c_arm=on (reboot required)"
  fi
else
  echo "    /boot/firmware/config.txt not found — this is expected on non-Pi hardware."
  echo "    On a Raspberry Pi, set 'dtparam=i2c_arm=on' in /boot/firmware/config.txt."
fi

# Generate a unique release ID using timestamp (or git commit / version)
RELEASE_ID="$(date +%Y%m%d_%H%M%S)"
RELEASE_DIR="$DEST/releases/$RELEASE_ID"
RELEASE_TAG="${ZUIL_RELEASE_TAG:-manual-$RELEASE_ID}"

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
printf '%s\n' "$RELEASE_TAG" > "$DEST/release-tag"

echo "==> Setting secure permissions"
chown -R zuil:zuil "$DEST" /var/cache/zuil

echo "==> Installing systemd units"
# Copy directly from the repository deploy/ folder or fallback to local dir
if [ -d "$DEPLOY/../deploy" ]; then
  cp "$DEPLOY/../deploy/zuil.service" "$DEPLOY/../deploy/zuil-kiosk.service" \
    "$DEPLOY/../deploy/zuil-update.service" "$DEPLOY/../deploy/zuil-update.timer" \
    "$DEPLOY/../deploy/zuil-update.path" "$DEPLOY/../deploy/zuil-update-tmpfiles.conf" \
    /etc/systemd/system/
  cp "$DEPLOY/../deploy/zuil-update-tmpfiles.conf" /etc/tmpfiles.d/zuil-update.conf
elif [ -d "$DEPLOY/deploy" ]; then
  cp "$DEPLOY/deploy/zuil.service" "$DEPLOY/deploy/zuil-kiosk.service" \
    "$DEPLOY/deploy/zuil-update.service" "$DEPLOY/deploy/zuil-update.timer" \
    "$DEPLOY/deploy/zuil-update.path" "$DEPLOY/deploy/zuil-update-tmpfiles.conf" \
    /etc/systemd/system/
  cp "$DEPLOY/deploy/zuil-update-tmpfiles.conf" /etc/tmpfiles.d/zuil-update.conf
elif [ -f "$DEPLOY/zuil.service" ]; then
  cp "$DEPLOY/zuil.service" "$DEPLOY/zuil-kiosk.service" \
    "$DEPLOY/zuil-update.service" "$DEPLOY/zuil-update.timer" \
    "$DEPLOY/zuil-update.path" "$DEPLOY/zuil-update-tmpfiles.conf" \
    /etc/systemd/system/
  cp "$DEPLOY/zuil-update-tmpfiles.conf" /etc/tmpfiles.d/zuil-update.conf
else
  echo "    WARNING: No systemd unit files found to install." >&2
fi

# Only talk to systemctl when systemd is actually running (e.g. not inside a
# container). On a real host this enables the services automatically.
if [ -d /run/systemd/system ]; then
  systemctl daemon-reload
  systemd-tmpfiles --create
  systemctl enable zuil.service zuil-kiosk.service
  systemctl enable --now zuil-update.timer
  systemctl enable --now zuil-update.path
else
  echo "    systemd not running on this host; enable services manually:"
  echo "      sudo systemctl daemon-reload"
  echo "      sudo systemctl enable --now zuil zuil-kiosk zuil-update.timer"
fi

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
