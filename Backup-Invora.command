#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
bash scripts/backup-now.sh
echo
read -r -n 1 -p "Backup finished. Press any key to close..."
echo
