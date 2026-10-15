#!/usr/bin/env bash

set -euo pipefail

DEST="/opt/zuil"
CURRENT="$DEST/current"


if [ ! -d "$CURRENT" ]; then
  echo "Error: No active release found at $CURRENT" >&2
  exit 1
fi

# Nothing to source: there is no configuration file and no environment file.
# The kiosk keeps everything in /var/lib/zuil and is set up from its own screen.

exec "$CURRENT/zuil"


