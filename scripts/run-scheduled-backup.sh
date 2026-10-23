#!/usr/bin/env bash
# Invoked by the owner's macOS LaunchAgent. Configuration lives outside Git.
set -euo pipefail
umask 077
export PATH="/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin"

repo_dir=$(cd "$(dirname "$0")/.." && pwd)
config="$repo_dir/.private-backups/backup-schedule.env"
if [[ ! -r "$config" ]]; then
  echo "Invora backup schedule configuration is missing: $config" >&2
  exit 1
fi
set -a
# The owner-only file contains backup paths and the rclone remote name.
# Shell syntax is intentional; do not accept configuration from other users.
source "$config"
set +a
: "${BACKUP_DESTINATION:?Schedule needs a backup destination}"
mkdir -p "$BACKUP_DESTINATION"
log="$BACKUP_DESTINATION/scheduled-backup.log"

if (cd "$repo_dir" && bash scripts/backup-offsite.sh) >> "$log" 2>&1; then
  date -u '+%Y-%m-%dT%H:%M:%SZ' > "$BACKUP_DESTINATION/last-success-utc"
  printf 'Invora encrypted offsite backup succeeded at %s\n' "$(cat "$BACKUP_DESTINATION/last-success-utc")"
else
  status=$?
  date -u '+%Y-%m-%dT%H:%M:%SZ' > "$BACKUP_DESTINATION/last-failure-utc"
  /usr/bin/osascript -e 'display notification "Check the backup log and retry the upload." with title "Invora backup failed"' >/dev/null 2>&1 || true
  printf 'Invora backup failed (exit %s); see %s\n' "$status" "$log" >&2
  exit "$status"
fi
