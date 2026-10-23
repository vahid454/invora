# Where Invora saves data and how it is protected

Invora stores one business in a PostgreSQL database, with branches inside it. In the local Windows/Mac installation the records stay on that computer. Invora does not automatically upload the shop database to a vendor cloud, synchronize computers, or send customer messages. WhatsApp/SMS buttons prepare a message that an operator chooses to send.

**Data-retention policy:** ordinary sale, purchase, payment, stock and ledger postings remain in PostgreSQL after app restarts, browser changes and image upgrades. Corrections use linked reversals or returns so the original posting stays reviewable. Product and device-detail edits are audited; access is limited by staff permissions. The owner controls the computer, Docker volumes, backups and retention. No vendor copy exists by default. Clearing a browser cache is safe for business records; deleting/resetting the database or Docker volumes is not. A shop administrator with host or database access can still delete records, so the application cannot promise that data will “never” be lost.

**A browser is only a window into the shop server.** Opening another browser on the same Mac uses that Mac's existing PostgreSQL database after signing in; clearing browser history or changing browsers does not remove invoices. Opening a browser on a Windows PC connected to the *same* HTTPS shop server also shows the same records and uses its own login session. Installing a second local copy on Windows creates a separate empty database unless you restore the Mac's database and uploads there. The app/EXE, browser profile and licence key do not synchronize records.

## What happens when you save

The browser or dedicated desktop window sends the sale or payment to the .NET API. The API checks the signed-in user's permissions and branch, recalculates amounts and tax, validates available stock, and saves the related invoice, stock, payment, ledger and audit entries together in one PostgreSQL transaction. A failed posting rolls back those entries. Request keys prevent the same successful request retry from double-posting.

The browser or dedicated desktop window displays saved records from the API. Closing a tab does not delete them. Invoice snapshots are saved in the database; PDFs can be generated again even if the original download was lost. Supplier/expense uploads are separate private files and need a separate archive alongside the database backup.

| Information | Storage in the local installation |
|---|---|
| Products, IMEI/serial identities, customers/suppliers, stock, invoices, payments, LenDen, expenses, staff and audit | PostgreSQL 18, Docker named volume `invora_database` |
| Uploaded supplier bills and expense documents | Docker named volume `invora_documents`, mounted at `/var/lib/invora/files` in the API |
| DB password, session-signing secret and first-setup secret | Private `.env` in the installation directory |
| Fresh Mac installation files/config | `~/Library/Application Support/Invora` |
| Fresh Windows installation files/config | `%LOCALAPPDATA%\Invora` |
| Downloaded PDFs/CSVs | The selected native/browser download folder; these copies need their own access protection |
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

Back up **the database, uploads and private configuration**, keeping matching versions. Stop API/web during the snapshot so uploads and their database entries agree; keep the database running. The supplied `scripts/backup.sh` checks the database dump and document archive before encrypting/signing them, checks the final archive, and removes an incomplete output if any step fails. `scripts/restore.sh` verifies its signature before restoring into a new isolated database. Configure the recipient/signing keys first, and preserve the necessary private decryption key and trusted verification key separately from backup output. A structurally valid archive still needs a real restore rehearsal.

Store multiple backup generations off the shop computer, protect `.env` separately, and rehearse restoration and reconciliation. A backup on the same disk cannot recover a lost or failed disk. Schedules, off-host transfer and failure alerts are not automatic in the desktop launcher; they must be configured for the shop. See [backup and restore instructions](deployment.md#backups).

For the optional second-drive/cloud copy command, provider choices, and an honest account of what is not configured yet, see [offsite backups](offsite-backups.md). `scripts/backup-offsite.sh` pauses billing for a consistent snapshot and copies the encrypted archive to configured destinations; it does not by itself install a schedule or create a cloud account.

**Before going live**, arrange a scheduled encrypted backup copied to a protected second device or cloud account, alert on a missed run, and do a trial restore. Keep the encryption private key and trusted signing public key separate from the shop PC. Until that is configured, the out-of-box installation protects ordinary transactions and restarts but has a single-machine failure point. The setup key and commercial licence do not change this.

For an owner-operated local shop, make an encrypted backup after each business day and before every app or computer change, copy it to at least one protected off-device destination, and check that a scheduled backup actually ran. Keep several generations so an accidental edit or corruption discovered later can be recovered. Periodically restore one into an isolated database and run `scripts/reconcile.sql`; checking only that a backup file exists does not prove recovery. Never delete the old computer's Docker volumes or reset it until the new installation, invoice PDFs, stock, balances and uploads have been checked against the restored copy.

## Moving to another Mac or Windows PC

1. Stop entering sales/payments on the old computer. Keep Docker's database running, stop API/web, and create a current encrypted database-and-documents backup. Copy the matching private `.env`, the required decryption/verification keys and the backup to protected **off-device** storage. Check the backup timestamp and retain older generations.
2. Install Invora and Docker Desktop on the new computer. Do not create a new shop or issue a new owner login. Restore the archive into an **isolated** database and document folder first, run `scripts/reconcile.sql`, compare key counts/balances, and verify an invoice PDF and an uploaded bill. The restore script deliberately never overwrites the live database.
3. Only after those checks, plan the cutover to the new Docker database/document volumes with the original `.env` and restored Shop ID. Verify login, inventory, invoices, LenDen and licence. Keep the old computer read-only until the new one is accepted; do not let two independent copies take new transactions.

The desktop app has **no automatic cross-device synchronization**. A fresh installation has no off-site backup until configured; this Mac now has a 9 PM local-time Backblaze push, described in [offsite backups](offsite-backups.md#this-macs-daily-backblaze-push). Records can still be lost after a disk failure between backups, a failed or missed scheduled run, or loss of the only decryption key. No software can guarantee “never lost”; check backup success, keep recovery keys away from the Mac, and periodically test restores. The signed shop licence is not a backup or an encryption key.

## Owner-requested cleanup on 8 October 2026

The local operational reset retained owner/staff accounts and grants, shop/branch/settings records, product models and variants, brands, categories, taxes, licences, audit history and document-number counters. Counters stay advanced to avoid reusing a previously issued number. It cleared customers/suppliers, notes/promises, stock and identities, purchases/sales/returns, payments/allocations, ledger, transfers, expenses, reviewed imports, operation replay receipts and uploaded files. Login passwords and `.env` were not changed. Active sessions were revoked; sign in again using the same login.

A private recovery snapshot was created under `.private-backups/before-clean-<UTC timestamp>` in the original workspace. Its database dump was restored into a temporary database, checked against the original table hashes/counts and reconciled; the selective reset was rehearsed there before touching live records. Preserved tables were verified again after reset, and all 32 operational tables and the document volume were confirmed empty. A new maintenance audit entry records the reset. No other application's database or Docker volumes were changed.

This one-time snapshot contains `database.dump`, `documents.tar.gz`, `configuration.env`, checksums, table hashes/counts and recovery notes. It has private local permissions but **is not itself encrypted**. Prior data also remains in retained audit history and recovery backups; this was a shop-data reset, not a secure-erasure operation. These folders are excluded from source packages and Docker builds. Do not distribute the snapshot with the software. Keep it only for the agreed recovery period and protect any off-computer copy.

## Owner-requested cleanup on 23 October 2026

The owner again requested clearing live shop transaction and customer data while keeping owner/staff logins, shop settings and the catalog. Before cleanup, a new signed encrypted database-and-uploads archive was uploaded to the private Backblaze bucket, downloaded, checksum-compared, restored in isolation and financially reconciled. The selective cleanup was rehearsed on a restored copy, then applied to the live database with API/web stopped. It cleared all 35 operational/session tables, including customers/suppliers, stock units, purchases, sales, payments, ledger, imports and active credentials; the document volume was empty. Protected shop/catalog tables matched their pre-clean row counts and fingerprints before the maintenance audit entry was added. Number sequences remained advanced, and the same owner login can be used again. Readiness and financial reconciliation passed after restart.

A fresh post-clean encrypted backup was uploaded by the 9 PM LaunchAgent's manual acceptance run, downloaded and checksum-compared, restored in isolation, and checked for empty operational tables and retained owner/catalog data. The pre-clean archive remains in the private bucket for recovery, so this cleanup is **not secure erasure**. The bucket does not contain `.env` or the decryption private key; those still need a separately protected off-computer recovery copy.

## Dedicated window and printed QR codes

Mac uses a persistent WebKit website profile; Windows uses `%LOCALAPPDATA%\Invora Desktop\WebView2`. These profiles hold session cookies/cache, not the primary stock/accounting database. Access tokens remain in application memory; refresh cookies still use the existing session controls. Sign out on a shared computer, use separate OS accounts, and protect the disk. The window itself does not add database encryption. It accepts only the configured local origin, has no JavaScript-to-native file bridge, and opens approved external links in the system browser. Downloaded invoices, CSVs and item labels are ordinary local files protected by the chosen folder's permissions.

Item QR codes contain only a typed, permanent stock-unit or product ID. Model and identity text is printed on the sticker, so anyone holding the item can read it. No customer data, credentials, purchase cost or selling price is encoded. QR lookup requires an authenticated user with inventory permission and authorized branch access. Copying a sticker does not create stock or permit a second sale of a sold unit. Labels are generated locally; no external QR service receives identifiers. Preserve the database during restore/migration so existing labels continue to resolve.
