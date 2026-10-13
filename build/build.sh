#!/usr/bin/env bash
#
# Produces the single deployable binary.
#
#   ./build/build.sh                  # default: linux-arm64 (Raspberry Pi)
#   ./build/build.sh linux-x64        # x86_64 Ubuntu desktop
#
# Output: artifacts/<rid>/zuil  — one self-contained file, nothing beside it.

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

echo
echo "Done: $OUT/zuil"
echo
echo "That is the whole deliverable: one file, nothing beside it. It keeps its"
echo "data in /var/lib/zuil and is set up entirely from its own screen."
echo "To install on a device, use scripts/installzuil.sh. See docs/deploy.md."
