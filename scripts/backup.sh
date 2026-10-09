#!/usr/bin/env bash
set -euo pipefail
umask 077
: "${BACKUP_PUBLIC_KEY:?Set the recipient public key for openssl encryption}"
: "${BACKUP_DESTINATION:?Set a private backup directory}"
mkdir -p "$BACKUP_DESTINATION"
stamp=$(date -u +%Y%m%dT%H%M%SZ)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
docker compose exec -T db sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom' > "$work/database.dump"
# During document archive, uploads must be paused or deployment stopped for a consistent file snapshot.
docker compose run --rm --no-deps --entrypoint tar api -czf - -C /var/lib/invora files > "$work/documents.tar.gz"
openssl rand -out "$work/key.bin" 32
openssl rand -out "$work/iv.bin" 16
openssl pkeyutl -encrypt -pubin -inkey "$BACKUP_PUBLIC_KEY" -pkeyopt rsa_padding_mode:oaep -pkeyopt rsa_oaep_md:sha256 -in "$work/key.bin" -out "$work/key.enc"
key=$(od -An -v -tx1 "$work/key.bin" | tr -d ' \n')
iv=$(od -An -v -tx1 "$work/iv.bin" | tr -d ' \n')
# Encrypt-then-sign: the RSA signature is verified before any restore/decryption.
tar -czf - -C "$work" database.dump documents.tar.gz | openssl enc -aes-256-cbc -K "$key" -iv "$iv" -out "$work/payload.enc"
cp "$work/iv.bin" "$work/iv"
: "${BACKUP_SIGNING_KEY:?Set a private signing key (separate from the recipient key)}"
cat "$work/key.enc" "$work/iv" "$work/payload.enc" > "$work/signed.bin"
openssl dgst -sha256 -sign "$BACKUP_SIGNING_KEY" -out "$work/signature" "$work/signed.bin"
tar -czf "$BACKUP_DESTINATION/invora-$stamp.backup" -C "$work" key.enc iv payload.enc signature
printf 'Encrypted backup created: %s\n' "$BACKUP_DESTINATION/invora-$stamp.backup"
