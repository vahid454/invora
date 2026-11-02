#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
echo "Creating encrypted Invora backup (database, uploads, and configuration)..."
bash scripts/run-scheduled-backup.sh
echo "Invora backup completed successfully."
