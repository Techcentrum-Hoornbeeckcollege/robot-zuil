#!/usr/bin/env bash

set -euo pipefail

DEST="/opt/zuil"
API_URL="https://api.github.com/repos/Techcentrum-Hoornbeeckcollege/robot-zuil/releases/latest"
ASSET_NAME="zuil-linux-arm64.zip"
WORK_DIR="$(mktemp -d /var/cache/zuil/update.XXXXXX)"
LOCK_FILE="/run/zuil-update.lock"
HEALTH_URL="http://localhost:5000/api/health"

cleanup() {
  rm -rf "$WORK_DIR"
}
trap cleanup EXIT

# Consume the trigger before doing any work. The path unit can then accept a
# later request, including one made while this update is running.
rm -f /run/zuil-update/request

exec 9>"$LOCK_FILE"
if ! flock -n 9; then
  echo "An update is already running; exiting."
  exit 0
fi

if [ "$(id -u)" -ne 0 ]; then
  echo "Run as root." >&2
  exit 1
fi

command -v curl >/dev/null 2>&1 || { echo "curl is required." >&2; exit 1; }
command -v sha256sum >/dev/null 2>&1 || { echo "sha256sum is required." >&2; exit 1; }
command -v unzip >/dev/null 2>&1 || { echo "unzip is required." >&2; exit 1; }

json="$(curl --fail --silent --show-error --location --max-time 30 \
  -H 'Accept: application/vnd.github+json' "$API_URL")"
tag="$(printf '%s' "$json" | sed -n 's/.*"tag_name":[[:space:]]*"\([^"]*\)".*/\1/p' | head -1)"

if [[ ! "$tag" =~ ^[A-Za-z0-9._/-]+$ ]]; then
  echo "Latest release returned an invalid tag." >&2
  exit 1
fi

installed_tag=""
if [ -f "$DEST/release-tag" ]; then
  installed_tag="$(tr -d '\r\n' < "$DEST/release-tag")"
fi
if [ "$tag" = "$installed_tag" ]; then
  echo "Already running release $tag."
  exit 0
fi

base_url="https://github.com/Techcentrum-Hoornbeeckcollege/robot-zuil/releases/download/$tag"
archive="$WORK_DIR/$ASSET_NAME"
checksums="$WORK_DIR/SHA256SUMS"
extract_dir="$WORK_DIR/release"

echo "Downloading release $tag."
curl --fail --silent --show-error --location --max-time 300 \
  "$base_url/$ASSET_NAME" -o "$archive"
curl --fail --silent --show-error --location --max-time 30 \
  "$base_url/SHA256SUMS" -o "$checksums"

(
  cd "$WORK_DIR"
  grep -F "  $ASSET_NAME" SHA256SUMS | sha256sum --check -
)

mkdir "$extract_dir"
unzip -q "$archive" -d "$extract_dir"
[ -x "$extract_dir/linux-arm64/zuil" ] || {
  echo "Release archive does not contain linux-arm64/zuil." >&2
  exit 1
}

previous_target="$(readlink -f "$DEST/current" 2>/dev/null || true)"
previous_tag="$(tr -d '\r\n' < "$DEST/release-tag" 2>/dev/null || true)"
if [ -z "$previous_target" ]; then
  echo "No active release is installed; run the installer first." >&2
  exit 1
fi

ZUIL_RELEASE_TAG="$tag" ZUIL_SKIP_DEPENDENCIES=1 \
  "$extract_dir/scripts/installzuil.sh" "$extract_dir/linux-arm64"

rollback() {
  echo "Update failed; rolling back to $previous_target." >&2
  ln -sfn "$previous_target" "$DEST/current"
  printf '%s\n' "$(basename "$previous_target")" > "$DEST/version"
  printf '%s\n' "$previous_tag" > "$DEST/release-tag"
  systemctl restart zuil.service zuil-kiosk.service || true
  exit 1
}

if ! systemctl restart zuil.service; then
  rollback
fi
for _ in $(seq 1 30); do
  if curl --fail --silent --max-time 2 "$HEALTH_URL" >/dev/null; then
    if ! systemctl restart zuil-kiosk.service; then
      rollback
    fi
    echo "Updated to release $tag."
    exit 0
  fi
  sleep 2
done

rollback
