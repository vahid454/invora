#!/usr/bin/env bash
# Run from the shop host with Docker Desktop running. Configure at least one
# destination outside the primary disk; no destination is created implicitly.
set -euo pipefail
umask 077

cd "$(dirname "$0")/.."
: "${BACKUP_DESTINATION:?Set a private local staging directory outside Docker volumes}"
: "${BACKUP_PUBLIC_KEY:?Set the recipient public key}"
: "${BACKUP_SIGNING_KEY:?Set the separate signing private key}"
[[ -r "$BACKUP_PUBLIC_KEY" && -f "$BACKUP_PUBLIC_KEY" ]] || { echo 'Recipient public key is not readable.' >&2; exit 1; }
[[ -r "$BACKUP_SIGNING_KEY" && -f "$BACKUP_SIGNING_KEY" ]] || { echo 'Signing private key is not readable.' >&2; exit 1; }

if [[ -z "${BACKUP_EXTERNAL_DIR:-}" && -z "${BACKUP_RCLONE_REMOTE:-}" ]]; then
  echo 'Set BACKUP_EXTERNAL_DIR and/or BACKUP_RCLONE_REMOTE; a backup on this computer alone is insufficient.' >&2
  exit 1
fi
if [[ -n "${BACKUP_EXTERNAL_DIR:-}" && ! -d "$BACKUP_EXTERNAL_DIR" ]]; then
  echo 'The second-drive directory is absent. Mount and check the drive first; it will not be created here.' >&2
  exit 1
fi
if [[ -n "${BACKUP_RCLONE_REMOTE:-}" ]]; then
  command -v rclone >/dev/null || { echo 'rclone is required for cloud copying.' >&2; exit 1; }
  [[ "$BACKUP_RCLONE_REMOTE" == *:* ]] || { echo 'BACKUP_RCLONE_REMOTE must be an rclone remote and bucket/path.' >&2; exit 1; }
fi
command -v docker >/dev/null || { echo 'Docker is required for the backup.' >&2; exit 1; }
mkdir -p "$BACKUP_DESTINATION"
[[ -w "$BACKUP_DESTINATION" ]] || { echo 'Local backup directory is not writable.' >&2; exit 1; }
if [[ -n "${BACKUP_EXTERNAL_DIR:-}" ]]; then
  local_device=$(df -P "$BACKUP_DESTINATION" | awk 'NR == 2 { print $1 }')
  second_device=$(df -P "$BACKUP_EXTERNAL_DIR" | awk 'NR == 2 { print $1 }')
  [[ "$local_device" != "$second_device" ]] || {
    echo 'Second-drive directory is on the same filesystem as the local backup.' >&2
    exit 1
  }
fi

lock="$BACKUP_DESTINATION/.backup-offsite.lock"
if ! mkdir "$lock" 2>/dev/null; then
  echo "Another backup may be running. Inspect the stale lock before removing: $lock" >&2
  exit 1
fi
run_dir=''
external_partial=''
restart_api=false
restart_web=false
cleanup() {
  result=$?
  trap - EXIT
  if [[ "$restart_api" == true ]]; then docker compose start api || result=1; fi
  if [[ "$restart_web" == true ]]; then docker compose start web || result=1; fi
  if [[ -n "$external_partial" ]]; then rm -f -- "$external_partial"; fi
  if [[ -n "$run_dir" ]]; then rm -rf -- "$run_dir"; fi
  rmdir -- "$lock"
  exit "$result"
}
trap cleanup EXIT
run_dir=$(mktemp -d "$BACKUP_DESTINATION/.backup-run.XXXXXX")
[[ -f .env && ! -L .env ]] || { echo 'Private shop .env is missing or linked; backup cannot be paired with its configuration.' >&2; exit 1; }
cp -- .env "$run_dir/shop.env"
chmod 600 "$run_dir/shop.env"

running=$(docker compose ps --services --status running)
if [[ $'\n'"$running"$'\n' == *$'\napi\n'* ]]; then restart_api=true; fi
if [[ $'\n'"$running"$'\n' == *$'\nweb\n'* ]]; then restart_web=true; fi
if [[ "$restart_web" == true ]]; then docker compose stop web; fi
if [[ "$restart_api" == true ]]; then docker compose stop api; fi

BACKUP_DESTINATION="$run_dir" bash scripts/backup.sh
shopt -s nullglob
archives=("$run_dir"/*.backup)
shopt -u nullglob
[[ ${#archives[@]} -eq 1 ]] || { echo 'Expected exactly one completed encrypted archive.' >&2; exit 1; }
archive="$BACKUP_DESTINATION/$(basename "${archives[0]}")"
[[ ! -e "$archive" ]] || { echo 'Refusing to overwrite a prior backup.' >&2; exit 1; }
mv -- "${archives[0]}" "$archive"

# Resume billing before potentially slow external/cloud transfers.
if [[ "$restart_api" == true ]]; then docker compose start api; restart_api=false; fi
if [[ "$restart_web" == true ]]; then docker compose start web; restart_web=false; fi

archive_name=$(basename "$archive")
if command -v shasum >/dev/null; then
  (cd "$BACKUP_DESTINATION" && shasum -a 256 "$archive_name" > "$archive_name.sha256")
else
  (cd "$BACKUP_DESTINATION" && sha256sum "$archive_name" > "$archive_name.sha256")
fi

if [[ -n "${BACKUP_EXTERNAL_DIR:-}" ]]; then
  [[ ! -e "$BACKUP_EXTERNAL_DIR/$archive_name" ]] || { echo 'Second-drive backup name already exists.' >&2; exit 1; }
  external_partial="$BACKUP_EXTERNAL_DIR/$archive_name.partial"
  cp "$archive" "$external_partial"
  cmp -s "$archive" "$external_partial" || { echo 'Second-drive copy did not verify.' >&2; exit 1; }
  mv "$external_partial" "$BACKUP_EXTERNAL_DIR/$archive_name"
  external_partial=''
  cp "$archive.sha256" "$BACKUP_EXTERNAL_DIR/$archive_name.sha256"
fi
if [[ -n "${BACKUP_RCLONE_REMOTE:-}" ]]; then
  remote="${BACKUP_RCLONE_REMOTE%/}/$archive_name"
  rclone copyto --immutable "$archive" "$remote"
  rclone copyto --immutable "$archive.sha256" "$remote.sha256"
fi

# Optional portable recovery file: encrypted DB/documents plus the matching
# .env and recovery keys. Upload the normal archive first so it survives even
# if portable packaging fails; a failed portable step still fails this job.
if [[ -n "${BACKUP_PORTABLE_KEY_FILE:-}" ]]; then
  : "${BACKUP_RECOVERY_PRIVATE_KEY:?Set the recipient private key for portable recovery}"
  : "${BACKUP_RECOVERY_SIGNING_PUBLIC_KEY:?Set the trusted signing public key for portable recovery}"
  command -v python3 >/dev/null || { echo 'Python 3 is required for portable recovery packaging.' >&2; exit 1; }
  cmp -s .env "$run_dir/shop.env" || { echo 'Shop .env changed during the backup; portable package was not created.' >&2; exit 1; }
  portable="$BACKUP_DESTINATION/${archive_name%.backup}.invora"
  python3 scripts/portable-recovery.py create \
    --backup "$archive" --env "$run_dir/shop.env" \
    --private-key "$BACKUP_RECOVERY_PRIVATE_KEY" \
    --signing-public-key "$BACKUP_RECOVERY_SIGNING_PUBLIC_KEY" \
    --recipient-public-key "$BACKUP_PUBLIC_KEY" \
    --signing-private-key "$BACKUP_SIGNING_KEY" \
    --key-file "$BACKUP_PORTABLE_KEY_FILE" --output "$portable"
  python3 scripts/portable-recovery.py verify "$portable" --key-file "$BACKUP_PORTABLE_KEY_FILE"
  portable_name=$(basename "$portable")
  if command -v shasum >/dev/null; then
    (cd "$BACKUP_DESTINATION" && shasum -a 256 "$portable_name" > "$portable_name.sha256")
  else
    (cd "$BACKUP_DESTINATION" && sha256sum "$portable_name" > "$portable_name.sha256")
  fi
  if [[ -n "${BACKUP_EXTERNAL_DIR:-}" ]]; then
    for item in "$portable" "$portable.sha256"; do
      name=$(basename "$item")
      [[ ! -e "$BACKUP_EXTERNAL_DIR/$name" ]] || { echo 'Portable recovery destination already exists.' >&2; exit 1; }
      external_partial="$BACKUP_EXTERNAL_DIR/$name.partial"
      cp "$item" "$external_partial"
      cmp -s "$item" "$external_partial" || { echo 'Second-drive portable copy did not verify.' >&2; exit 1; }
      mv "$external_partial" "$BACKUP_EXTERNAL_DIR/$name"
      external_partial=''
    done
  fi
  if [[ -n "${BACKUP_RCLONE_REMOTE:-}" ]]; then
    rclone copyto --immutable "$portable" "${BACKUP_RCLONE_REMOTE%/}/$portable_name"
    rclone copyto --immutable "$portable.sha256" "${BACKUP_RCLONE_REMOTE%/}/$portable_name.sha256"
  fi
  printf 'Password-protected one-file recovery package saved and copied: %s\n' "$portable"
fi

printf 'Verified encrypted backup saved locally and copied to every configured destination: %s\n' "$archive"
