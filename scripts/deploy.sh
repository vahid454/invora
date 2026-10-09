#!/usr/bin/env bash
set -euo pipefail
: "${INVORA_BROWSER_ORIGIN:?Configure HTTPS browser origin}"
[[ "$INVORA_BROWSER_ORIGIN" == https://* ]] || { echo 'Production browser origin must use HTTPS.' >&2; exit 1; }
compose=(docker compose -f docker-compose.yml -f docker-compose.production.yml)
"${compose[@]}" build
"${compose[@]}" up -d db
# Back up an existing deployment before running this script; migrations are not rolled back automatically.
"${compose[@]}" run --rm migrate
"${compose[@]}" up -d api web
for attempt in {1..30}; do
  if curl --fail --silent "${INVORA_BROWSER_ORIGIN}/health/ready" > /dev/null; then echo 'Deployment ready.'; exit 0; fi
  sleep 2
done
echo 'Readiness failed. Inspect containers; do not restore or down-migrate automatically.' >&2
exit 1
