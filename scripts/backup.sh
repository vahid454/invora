#!/usr/bin/env bash
set -euo pipefail
umask 077
: "${BACKUP_PUBLIC_KEY:?Set the recipient public key for openssl encryption}"
: "${BACKUP_DESTINATION:?Set a private backup directory}"
: "${BACKUP_SIGNING_KEY:?Set a private signing key (separate from the recipient key)}"
[[ -f "$BACKUP_PUBLIC_KEY" && -r "$BACKUP_PUBLIC_KEY" ]] || { echo 'Recipient public key is not readable.' >&2; exit 1; }
[[ -f "$BACKUP_SIGNING_KEY" && -r "$BACKUP_SIGNING_KEY" ]] || { echo 'Signing key is not readable.' >&2; exit 1; }
mkdir -p "$BACKUP_DESTINATION"
stamp=$(date -u +%Y%m%dT%H%M%SZ)
work=$(mktemp -d)
archive_tmp=$(mktemp "$BACKUP_DESTINATION/invora-$stamp-XXXXXX")
archive="$archive_tmp.backup"
complete=false
cleanup(){ rm -rf "$work"; if [[ "$complete" != true ]]; then rm -f "$archive" "$archive_tmp"; fi; }
trap cleanup EXIT
docker compose exec -T db sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom' > "$work/database.dump"
[[ -s "$work/database.dump" ]] && docker compose exec -T db pg_restore --list < "$work/database.dump" > /dev/null || { echo 'Database dump verification failed.' >&2; exit 1; }
# During document archive, uploads must be paused or deployment stopped for a consistent file snapshot.
docker compose run --rm --no-deps --entrypoint tar api -czf - -C /var/lib/invora files > "$work/documents.tar.gz"
tar -tzf "$work/documents.tar.gz" > /dev/null || { echo 'Document archive verification failed.' >&2; exit 1; }
openssl rand -out "$work/key.bin" 32
openssl rand -out "$work/iv.bin" 16
openssl pkeyutl -encrypt -pubin -inkey "$BACKUP_PUBLIC_KEY" -pkeyopt rsa_padding_mode:oaep -pkeyopt rsa_oaep_md:sha256 -in "$work/key.bin" -out "$work/key.enc"
key=$(od -An -v -tx1 "$work/key.bin" | tr -d ' \n')
iv=$(od -An -v -tx1 "$work/iv.bin" | tr -d ' \n')
# Encrypt-then-sign: the RSA signature is verified before any restore/decryption.
tar -czf - -C "$work" database.dump documents.tar.gz | openssl enc -aes-256-cbc -K "$key" -iv "$iv" -out "$work/payload.enc"
cp "$work/iv.bin" "$work/iv"
cat "$work/key.enc" "$work/iv" "$work/payload.enc" > "$work/signed.bin"
openssl dgst -sha256 -sign "$BACKUP_SIGNING_KEY" -out "$work/signature" "$work/signed.bin"
tar -czf "$archive_tmp" -C "$work" key.enc iv payload.enc signature
tar -tzf "$archive_tmp" > /dev/null || { echo 'Final backup archive verification failed.' >&2; exit 1; }
mv "$archive_tmp" "$archive"
complete=true
printf 'Encrypted backup created and structurally verified: %s\n' "$archive"
