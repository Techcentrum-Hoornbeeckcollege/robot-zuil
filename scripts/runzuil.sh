#!/usr/bin/env bash

set -euo pipefail

DEST="/opt/zuil"
CURRENT="$DEST/current"


if [ ! -d "$CURRENT" ]; then
  echo "Error: No active release found at $CURRENT" >&2
  exit 1
fi

if [ -f "$DEST/config/.env" ]; then
  set -a
  source "$DEST/config/.env"
  set +a
fi

exec "$CURRENT/zuil"


