# Where Invora saves data and how it is protected

Invora stores one business in a PostgreSQL database, with branches inside it. In the local Windows/Mac installation the records stay on that computer. Invora does not automatically upload the shop database to a vendor cloud, synchronize computers, or send customer messages. WhatsApp/SMS buttons prepare a message that an operator chooses to send.

## What happens when you save

The browser sends the sale or payment to the .NET API. The API checks the signed-in user's permissions and branch, recalculates amounts and tax, validates available stock, and saves the related invoice, stock, payment, ledger and audit entries together in one PostgreSQL transaction. A failed posting rolls back those entries. Request keys prevent the same successful request retry from double-posting.

The browser displays saved records from the API. Closing a tab does not delete them. Invoice snapshots are saved in the database; PDFs can be generated again even if the original download was lost. Supplier/expense uploads are separate private files and need a separate archive alongside the database backup.

| Information | Storage in the local installation |
|---|---|
| Products, IMEI/serial identities, customers/suppliers, stock, invoices, payments, LenDen, expenses, staff and audit | PostgreSQL 18, Docker named volume `invora_database` |
| Uploaded supplier bills and expense documents | Docker named volume `invora_documents`, mounted at `/var/lib/invora/files` in the API |
| DB password, session-signing secret and first-setup secret | Private `.env` in the installation directory |
| Fresh Mac installation files/config | `~/Library/Application Support/Invora` |
| Fresh Windows installation files/config | `%LOCALAPPDATA%\Invora` |
| Downloaded PDFs/CSVs | The browser's selected download folder; these copies need their own access protection |
| Vendor licence-signing private key | Vendor machine only; never in a customer release |

On Docker Desktop, volumes are inside its managed Linux VM disk. Docker Desktop's Volumes and storage settings identify their current location; a hardcoded host path may be wrong after moving Docker's disk image. [Docker's recovery guide](https://docs.docker.com/desktop/settings-and-maintenance/backup-and-restore/) explains VM/volume recovery. Copying the EXE, app, source directory or container image alone does not back up these records.

## Protections implemented

- **Login:** salted ASP.NET Identity password hashes, configured for 210,000 PBKDF2 iterations; no recoverable plaintext owner password. Login rate limits and temporary lockout reduce repeated guessing.
- **Sessions:** ten-minute access tokens, rotating refresh credentials stored as hashes, 14-day session limits, replay revocation, HttpOnly/SameSite cookies and origin/CSRF checks. Browser access tokens stay in memory rather than local storage.
- **Staff access:** the API verifies permissions and allowed branches on requests. Give employees individual accounts; hiding a button is not the permission check.
- **Stock and money:** database constraints, uniqueness checks, transaction locking, request replay protection and append-only guards protect normal posting. Corrections and returns retain linked history.
- **Documents:** authenticated branch-scoped downloads; uploaded bills are not public web files. Upload size and file signatures are checked.
- **Local exposure:** only the web endpoint is bound to `127.0.0.1`; PostgreSQL and API ports are not published. New launcher configuration uses independent random secrets and owner-only file permissions. Private configuration, backups and signing keys are excluded from customer packages and Docker build context.

## Security boundaries

The local launcher uses HTTP on this computer's loopback interface and the Development profile. It does not provide HTTPS across a shop network. Multi-computer, LAN or internet access requires the production HTTPS setup in [deployment](deployment.md).

**The application does not encrypt ordinary database records or uploads at rest.** Enable host disk encryption and keep recovery credentials separately. On Mac, use [FileVault](https://support.apple.com/en-gb/guide/mac-help/mh11785/26/mac); on Windows use your supported disk-encryption configuration. FileVault was confirmed **On** on this development Mac on 8 October 2026. That does not encrypt an exported dump once it is copied onto an unencrypted device.

The local Compose configuration uses the database owner credentials for the API. The production overlay supports a separate restricted runtime database role; it is not automatically enabled by the desktop launchers. A machine administrator or someone with Docker/database-owner access can read data and bypass database history guards. Audits are useful application history, not tamper-proof evidence against administrators. There is no MFA or provider-backed password recovery in this release.

The setup key creates the first owner and is closed after setup. The signed commercial licence enables recording for the agreed period. **Neither key encrypts customer records.** Keep configuration secrets private; give the vendor only the Shop ID needed for licensing.

## Backups that can recover the shop

Back up **the database, uploads and private configuration**, keeping matching versions. Stop API/web during the snapshot so uploads and their database entries agree; keep the database running. The supplied `scripts/backup.sh` encrypts and signs the database-plus-documents archive, and `scripts/restore.sh` verifies it before restoring into a new isolated database. Configure the recipient/signing keys first, and preserve the necessary private decryption key and trusted verification key separately from backup output.

Store multiple backup generations off the shop computer, protect `.env` separately, and rehearse restoration and reconciliation. A backup on the same disk cannot recover a lost or failed disk. Schedules, off-host transfer and failure alerts are not automatic in the desktop launcher; they must be configured for the shop. See [backup and restore instructions](deployment.md#backups).

## Owner-requested cleanup on 8 October 2026

The local operational reset retained owner/staff accounts and grants, shop/branch/settings records, product models and variants, brands, categories, taxes, licences, audit history and document-number counters. Counters stay advanced to avoid reusing a previously issued number. It cleared customers/suppliers, notes/promises, stock and identities, purchases/sales/returns, payments/allocations, ledger, transfers, expenses, reviewed imports, operation replay receipts and uploaded files. Login passwords and `.env` were not changed. Active sessions were revoked; sign in again using the same login.

A private recovery snapshot was created under `.private-backups/before-clean-<UTC timestamp>` in the original workspace. Its database dump was restored into a temporary database, checked against the original table hashes/counts and reconciled; the selective reset was rehearsed there before touching live records. Preserved tables were verified again after reset, and all 32 operational tables and the document volume were confirmed empty. A new maintenance audit entry records the reset. No other application's database or Docker volumes were changed.

This one-time snapshot contains `database.dump`, `documents.tar.gz`, `configuration.env`, checksums, table hashes/counts and recovery notes. It has private local permissions but **is not itself encrypted**. Prior data also remains in retained audit history and recovery backups; this was a shop-data reset, not a secure-erasure operation. These folders are excluded from source packages and Docker builds. Do not distribute the snapshot with the software. Keep it only for the agreed recovery period and protect any off-computer copy.
