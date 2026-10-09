# Deployment and recovery

## Local full stack

Mac uses **Invora.app**, distributed in `Invora-Mac.dmg`; see [Mac installation](macos-installation.md). Both desktop launchers use the same persistent volumes and require Docker Desktop. See [data storage and security](data-storage-security.md) for local encryption and recovery boundaries.

Windows uses the same containers through `InvoraSetup.exe` and `Start-Invora.cmd`; see [Windows installation](windows-installation.md). See the [owner guide](owner-guide.md) for request/data flow, persistent volume locations and security boundaries, and [commercial release](commercial-release.md) for signed licence configuration. The executable embeds the release ZIP, which excludes live data, `.env`, issued licence files and vendor signing keys. Docker Desktop remains a prerequisite.

`python3 scripts/dev.py` generates independent private local secrets only when `.env` is absent. It builds `invora-api:local` and `invora-web:local`, starts PostgreSQL, runs the explicit migration bundle, and serves the workspace at `http://127.0.0.1:8080`. There is no automatic schema migration inside API startup. Local setup is `/setup` with the private bootstrap key; the owner chooses their password.

Container configuration uses .NET SDK 10.0.401/runtime 10.0.12, Node 24.21.0, Nginx 1.30.5 stable and PostgreSQL 18.6. Dependency lockfiles are enforced. The API runs as the .NET nonroot application user. Database and document volumes persist independently of container recreation.

## Production preparation

The repository supplies `docker-compose.yml`, `docker-compose.production.yml`, TLS Nginx configuration, an explicit migration container, a restricted runtime-role SQL file, readiness checks and `scripts/deploy.sh`. It does not create a Lightsail instance, DNS records, certificates or an off-host backup bucket/account.

On the chosen Linux host:

1. Install Docker/Compose, copy this project/release, and create private configuration from `.env.example`. Use independent generated secrets and `INVORA_BROWSER_ORIGIN=https://your-domain`. Keep `POSTGRES_USER` as the migration/database owner; choose a separate `INVORA_RUNTIME_PASSWORD`.
2. Configure DNS and real TLS certificates. `INVORA_TLS_DIRECTORY` must contain `fullchain.pem` and `privkey.pem`; `INVORA_ACME_DIRECTORY` is the renewal challenge webroot. The TLS overlay exposes only HTTP/HTTPS publicly. Database and API ports remain private. Configure and test certificate renewal independently of the app.
3. Build images, start `db`, and run `migrate` using the base Compose configuration. This uses the migration owner, never the runtime user.
4. Run `deployment/runtime-role.sql` once through `psql`, supplying `runtime_password` and `database_name` variables privately. It creates `invora_runtime`, grants only required data permissions, and excludes writing migration history. Review/reapply grants when a future migration adds a table.
5. Run `scripts/deploy.sh` with the production variables exported. It uses the production overlay, runs explicit migrations, starts API/web and checks HTTPS readiness. Verify setup/login and a reviewed business smoke transaction before pilot use.
6. Keep the bootstrap secret private after setup; the current Compose configuration still requires the setting. The singleton guard closes setup after the first owner. Configure off-host backups/retention/alerts and complete hardware/store acceptance.

The production overlay runs the API with `invora_runtime`. PostgreSQL triggers protect posted history; the runtime role cannot own tables, migrate or disable those triggers. The migration owner is reserved for reviewed schema changes. Audit/history is not tamper-proof against a privileged database administrator.

Nginx overwrites forwarded client IP and the API trusts only the configured internal proxy address. The Compose network uses `172.30.0.0/24` and web address `172.30.0.4`; update both network and `Proxy__TrustedAddress` if this conflicts with the host's networking.

Use immutable release tags through `INVORA_TAG` in a deployment pipeline. For migrations that require a backfill, inspect the generated SQL and take a backup first. `SupplierInvoiceFinancialYear` deliberately backfills the fiscal-year column inside its migration transaction before adding the unique constraint. Existing inconsistent duplicate supplier references must be reviewed; no records are discarded to force a migration through.

## Backups

`scripts/backup.sh` creates a PostgreSQL custom dump and document archive, encrypts them with a random AES key protected by an RSA-OAEP recipient key, and signs the encrypted key/IV/payload with a separate signing key. Backups are private by default (`umask 077`). Keep the recipient private key and trusted signing public key separate from the host's backup output.

Set:

- `BACKUP_PUBLIC_KEY`: recipient RSA public key file.
- `BACKUP_SIGNING_KEY`: separate private RSA signing key file.
- `BACKUP_DESTINATION`: private local staging directory.
- `COMPOSE_ENV_FILES`: private Compose configuration path, if it is not the default `.env`.

Run during a maintenance window with writes/uploads stopped. A PostgreSQL dump is internally consistent; quiescing uploads ensures that every committed file record has a corresponding archived blob. Preserve the archive intact, transfer it to a configured private off-host destination, verify freshness, and apply the chosen retention schedule. The script does not invent an off-host account or scheduler.

## Restore rehearsal

`scripts/restore.sh path/to/archive.backup` verifies the signature **before decryption**, decrypts the archive, creates a fresh database and restores its documents into an isolated directory. It refuses database names that do not start with `invora_restore_`; `createdb` fails if the chosen destination exists. It never overwrites the live database.

Required environment: `RESTORE_PRIVATE_KEY`, `RESTORE_SIGNING_PUBLIC_KEY`, `RESTORE_DATABASE`, `RESTORE_DOCUMENTS`, and the private Compose environment if needed.

Run `scripts/reconcile.sql` against the restored database. Each query returns only discrepancies; check row counts, migration history, private file availability and invoice PDFs as well. Redirect live traffic only after a reviewed recovery decision. A database restoration can lose transactions since the backup and is not an ordinary code rollback.

A local rehearsal on 6 October 2026 verified encryption/signature/restore against an isolated PostgreSQL database. All supplied reconciliation queries returned zero discrepancies. This is evidence for the mechanism; live backup scheduling, off-host transfer, retention and measured production RPO/RTO remain host-specific tasks.

## Monitoring and rollback

Use `/health/live` for process liveness and `/health/ready` for database readiness. Monitor failed postings, HTTP errors, certificate expiry, disk space, database latency and backup freshness. Public health output contains no connection secrets. Logs record request outcomes without password/token bodies.

A failed deployment script leaves the database intact and asks the operator to inspect readiness. Roll back application images only when the schema is compatible; do not automatically down-migrate or restore a live database. CI verifies unit/integration tests, model/migration alignment, UI build and both images before a release is packaged.
