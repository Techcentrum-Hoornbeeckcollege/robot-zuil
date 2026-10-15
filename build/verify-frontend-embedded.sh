#!/usr/bin/env bash
#
# Fails if the frontend did not end up inside the binary.
#
#   ./build/verify-frontend-embedded.sh artifacts/linux-arm64/zuil
#
# The whole deliverable is one file, so "did the frontend get built" and "did
# the frontend get into the exe" are different questions and only the second
# one matters. A publish with an empty src/Zuil.Host/wwwroot succeeds, produces
# a binary of very nearly the right size, and then serves the SPA fallback's
# "Frontend not built" 500 to every visitor — on a wall-mounted kiosk, hours
# after anyone could have noticed. Nothing else in the build would catch it.
#
# Greps the binary rather than running it: the resource names below are what
# Microsoft.Extensions.FileProviders.Embedded writes into the assembly, and
# ManifestEmbeddedFileProvider looks them up by exactly those names at startup.

set -euo pipefail

BIN="${1:?usage: verify-frontend-embedded.sh <path-to-binary>}"

if [ ! -f "$BIN" ]; then
  echo "No binary at $BIN" >&2
  exit 1
fi

# -a because the haystack is an executable; grep would otherwise call it binary
# and print nothing but a summary line.
require() {
  if ! grep -aqE "$1" "$BIN"; then
    echo "  MISSING: $2" >&2
    return 1
  fi
  echo "  OK: $2"
}

echo "==> Verifying the frontend is embedded in $BIN"

failed=0
require 'Zuil\.Host\.wwwroot\.index\.html' 'wwwroot/index.html' || failed=1
require 'Zuil\.Host\.wwwroot\.assets\.index-[^.]+\.js' 'a fingerprinted JS bundle' || failed=1
require 'Zuil\.Host\.wwwroot\.assets\.index-[^.]+\.css' 'a fingerprinted CSS bundle' || failed=1

if [ "$failed" -ne 0 ]; then
  echo >&2
  echo "The binary has no frontend in it. build/build.sh stages frontend/dist" >&2
  echo "into src/Zuil.Host/wwwroot before publishing; check that the npm build" >&2
  echo "ran and that the csproj still embeds wwwroot/**/*." >&2
  exit 1
fi

echo "Frontend embedded."
