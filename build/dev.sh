#!/usr/bin/env bash
#
# Development loop: backend with fakes on :5000, Vite with HMR on :5173.
# No Entra tenant, no Pi, no ESP32 required. Open http://localhost:5173.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cleanup() {
  # Kill the whole process group so Vite does not survive Ctrl-C.
  jobs -p | xargs -r kill 2>/dev/null || true
}
trap cleanup EXIT INT TERM

echo "==> Backend on http://localhost:5000 (fake calendar + fake device)"
dotnet run --project "$ROOT/src/Zuil.Host" -- --fake-calendar --fake-device --no-kiosk &

echo "==> Frontend on http://localhost:5173"
cd "$ROOT/frontend"
[ -d node_modules ] || npm ci
npm run dev

wait
