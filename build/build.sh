#!/usr/bin/env bash
#
# Produces the single deployable binary.
#
#   ./build/build.sh                  # default: linux-arm64 (Raspberry Pi)
#   ./build/build.sh linux-x64        # x86_64 Ubuntu desktop
#
# Output: artifacts/<rid>/zuil  plus the config/ directory it needs beside it.

set -euo pipefail

RID="${1:-linux-arm64}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/artifacts/$RID"
WWWROOT="$ROOT/src/Zuil.Host/wwwroot"

echo "==> Building frontend"
cd "$ROOT/frontend"
npm ci
npm run build

echo "==> Staging frontend into the host project"
# Everything here becomes an embedded resource in the binary, so clear out the
# previous build rather than letting stale fingerprinted assets accumulate.
find "$WWWROOT" -mindepth 1 ! -name '.gitkeep' -delete
cp -r "$ROOT/frontend/dist/." "$WWWROOT/"

echo "==> Publishing single-file binary for $RID"
cd "$ROOT"
rm -rf "$OUT"
dotnet publish src/Zuil.Host/Zuil.Host.csproj \
  -c Release \
  -p:TargetRid="$RID" \
  -o "$OUT"

echo "==> Staging runtime configuration"
# Config ships next to the binary, not inside it: room directions change with
# the building and credentials must never be compiled in.
mkdir -p "$OUT/config"
cp "$ROOT/config/rooms.json" "$OUT/config/"
cp "$ROOT/config/appsettings.Production.json.example" "$OUT/config/"

echo
echo "Done: $OUT/zuil"
echo
echo "To install on the device, use scripts/installzuil.sh. It seeds the config"
echo "directory and generates an admin password; the tenant, client id and client"
echo "secret are then entered from the kiosk itself. See docs/deploy.md."
