# Windows release acceptance

Status: **pending on a native Windows x64 PC**. The development host is macOS. Cross-publishing, archive verification and simulated PowerShell launcher checks do not verify Windows PowerShell 5.1, Explorer, NTFS permissions, WSL 2 or Docker Desktop.

Use a dedicated test Windows account/PC with synthetic shop records. Keep its Docker context separate from any live shop. Record Windows/Docker versions, release SHA-256, operator/date and results without recording passwords, setup keys or private signing keys.

| Check | Expected result | Result |
|---|---|---|
| Verify the delivered executable | `Get-FileHash .\InvoraSetup.exe -Algorithm SHA256` agrees with the vendor's trusted checksum; `InvoraSetup.exe --verify-package` succeeds | Pending |
| Docker unavailable/not started | Installer gives a readable action and stays open after failure; no installation/data reset | Pending |
| First install from Explorer | Shows per-user folder, accepts Enter, builds, migrates and opens the local browser | Pending |
| Private configuration | `.env` is created once; `Get-Acl -LiteralPath .env` shows current user and SYSTEM with inheritance disabled; do not display its contents in evidence | Pending |
| Desktop shortcuts | Start, Stop and Check Invora work from a folder containing spaces, using the correct working directory | Pending |
| First owner and licence | Setup creates exactly one owner/business; sign-in works; paid shop-ID licence enables recording | Pending |
| Read-only diagnosis | Check Invora shows readiness and saves a report without configuration values, customer information or raw logs | Pending |
| Synthetic counter flow | Receive two differently coloured/identified phones; correct one; sell, accept extra payment, return and refund; reconcile stock/money; review new/used warranty PDFs | Pending |
| Daily restart without internet | Stop/restart Docker and Invora with existing images; same login, stock, accounts, documents and licence remain accessible | Pending |
| Update while containers stopped | Back up database/documents; rerun the installer; it finds the original folder and preserves `.env` bytes, Shop ID, records and uploads | Pending |
| Build/startup failure | Console remains visible with next steps; retry succeeds after fixing the cause; configuration and volumes survive | Pending |
| Invalid/local conflicting port | Invalid/mismatched configuration fails before migration; a free port with matching browser origin works; port conflict gives a readable startup failure | Pending |
| Missing original configuration | On the isolated test copy, temporarily move `.env` to a private recovery location while retaining volumes; installer/startup refuse replacement credentials; Check reports the problem; restore the file | Pending |
| Recovery | Restore the encrypted database and document backup to a separate environment; owner verifies invoices, ledger totals, identities and private files | Pending |
| Physical scanner and printer | Complete [counter hardware acceptance](testing.md#at-counter-hardware-acceptance), including a real A4 print and rapid scans | Pending |

Keep backup/recovery experiments on the test copy. Do not delete volumes or reset Docker to solve an installation problem. A failure should retain its check report and a description of the step; inspect raw startup errors locally before sharing them because command output can contain private paths.

Public delivery remains pending native acceptance and vendor code signing. The included SHA-256 manifest detects corrupted files; trusted distribution and executable signing establish the publisher.
