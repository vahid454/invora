#!/usr/bin/env bash
set -euo pipefail
umask 077
: "${RESTORE_PRIVATE_KEY:?Set recipient private key}"
: "${RESTORE_SIGNING_PUBLIC_KEY:?Set trusted backup signing public key}"
: "${RESTORE_DATABASE:?Set an empty isolated destination database}"
[[ "$RESTORE_DATABASE" =~ ^invora_restore_[a-zA-Z0-9_]+$ ]] || { echo 'Restore destination must start with invora_restore_.' >&2; exit 1; }
archive=${1:?Pass encrypted backup archive}
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
tar -xzf "$archive" -C "$work" key.enc iv payload.enc signature
cat "$work/key.enc" "$work/iv" "$work/payload.enc" > "$work/signed.bin"
openssl dgst -sha256 -verify "$RESTORE_SIGNING_PUBLIC_KEY" -signature "$work/signature" "$work/signed.bin"
openssl pkeyutl -decrypt -inkey "$RESTORE_PRIVATE_KEY" -pkeyopt rsa_padding_mode:oaep -pkeyopt rsa_oaep_md:sha256 -in "$work/key.enc" -out "$work/key.bin"
key=$(od -An -v -tx1 "$work/key.bin" | tr -d ' \n')
iv=$(od -An -v -tx1 "$work/iv" | tr -d ' \n')
openssl enc -d -aes-256-cbc -K "$key" -iv "$iv" -in "$work/payload.enc" -out "$work/archive.tar.gz"
tar -xzf "$work/archive.tar.gz" -C "$work" database.dump documents.tar.gz
docker compose exec -T -e RESTORE_DATABASE="$RESTORE_DATABASE" db sh -c 'createdb -U "$POSTGRES_USER" "$RESTORE_DATABASE"'
docker compose exec -T -e RESTORE_DATABASE="$RESTORE_DATABASE" db sh -c 'pg_restore -U "$POSTGRES_USER" -d "$RESTORE_DATABASE" --no-owner --no-acl --exit-on-error' < "$work/database.dump"
mkdir -p "${RESTORE_DOCUMENTS:?Set a private isolated restore directory}"
tar -xzf "$work/documents.tar.gz" -C "$RESTORE_DOCUMENTS"
printf 'Restored isolated database %s. Run reconciliation before changing live traffic.\n' "$RESTORE_DATABASE"
