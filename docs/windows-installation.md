# Install and run Invora on Windows

The Windows release includes **`InvoraSetup.exe`** for a local shop and **`Invora.Desktop.exe`** as the desktop window. The local installer runs .NET 10, Angular and PostgreSQL through Docker Desktop. For a separately provisioned hosted HTTPS shop, distribute only the desktop executable: the customer enters the shop address and does not install Docker. The hosted server and backup policy must exist first. See [deployment modes](deployment-modes.md). A source ZIP and CMD launchers are also provided for local installations.

## Hosted shop (no Docker on the PC)

First rebuild `tools/Invora.Desktop` from the current source with .NET 10; the older executable already in `artifacts/windows-desktop` predates hosted connection support. After vendor code signing and Windows acceptance, give the customer that rebuilt `Invora.Desktop.exe`. The customer opens it directly, enters the shop's root HTTPS address on first launch, and signs in. The app remembers the address; `Invora.Desktop.exe --configure-hosted` changes it. It can also be launched with `--hosted-url https://shop.example.com/`. Microsoft WebView2 Runtime is required. The production server owns the PostgreSQL database and uploaded documents, so internet is required for billing. Do not distribute or run `InvoraSetup.exe` for this hosted-only path.

## First installation

1. Install and start [Docker Desktop for Windows](https://docs.docker.com/desktop/setup/install/windows-install/). Follow its current Windows/WSL 2 requirements, enable virtualization if requested, and use **Linux containers**. Review Docker Desktop's licence terms for your organization. No local .NET SDK, Node or Python installation is needed for this launcher.
2. Download `InvoraSetup.exe` from your vendor and run it. The installer verifies its embedded release before writing files and shows the installation folder; press Enter to continue. A fresh installation defaults to `%LOCALAPPDATA%\Invora`, a private folder under your Windows account. Existing installations are reused when detected; use `InvoraSetup.exe --install-dir C:\Invora` if your original folder is different. The installer remembers its folder for later updates. It refuses to create a new configuration for existing Docker volumes when the original `.env` is missing. The current build is unsigned: validate its source/vendor and complete Windows acceptance; public releases should use the vendor's trusted code-signing certificate.
3. The installer creates **Start Invora / Stop Invora / Check Invora** desktop shortcuts and starts the app. Startup generates independent random secrets only when `.env` is absent, restricts access to that new file, builds images, starts PostgreSQL, runs explicit migrations, and opens the Invora shop window. The internal address is `http://127.0.0.1:8080`; there is no address bar in the app. The first build downloads dependencies and needs internet. Later daily starts use the built images. If using the ZIP instead, extract it into a permanent private folder and double-click `Start-Invora.cmd`.
4. On the first run choose **Set up Invora** on sign-in (or open `/setup` using the browser fallback), use `INVORA_BOOTSTRAP_KEY` from the private `.env`, and create the business and owner login/password. There is no preset owner password. After setup succeeds, it cannot create a second owner/business through the setup page.
5. For a licensed commercial release, open **Shop licence**, send your **Shop ID** to the vendor and paste the signed licence key they issue after the agreed payment. Activation enables recording. Your setup key is private and is not a licence key.
6. Configure Business settings: legal name, address, GST registration/state/GSTIN as applicable, bank and invoice terms. Create individual employee accounts and grant only the necessary branches and actions.
7. Count opening stock, or receive the actual supplier bills. Enter each phone's IMEI, memory and colour in its own device row. Review an invoice PDF and a controlled payment/return with the shop operator before normal use.

For a licensed commercial release, complete activation before recording transactions: open **Shop licence**, send only your **Shop ID** to the vendor, receive the signed licence key after the agreed payment, then paste it into **Licence key → Activate licence**. Your setup key creates the owner; it is not a licence key and should not be sent to the vendor. Expiry keeps records/reports/exports available; a current renewal restores recording. See [commercial licences](commercial-release.md).

The PC must remain running, with Docker Desktop running, for the application to be available. Once images are built, ordinary local billing does not require internet; WhatsApp, remote backups and initial dependency downloads do. The local launcher uses a loopback development profile for HTTP and does not install a Windows service or configure remote access.

The dedicated window requires Microsoft Edge **WebView2 Evergreen Runtime**, normally present on supported Windows systems. If missing, Invora offers the [official runtime download](https://developer.microsoft.com/microsoft-edge/webview2/). Install it, then reopen Invora. **Open in browser** and `Open-InvoraBrowser.cmd` remain available. PDF/CSV downloads ask for a save location; print the saved PDF with the usual Windows print dialog.

## Daily start, stop and updates

Double-click `Start-Invora.cmd` to start and open the app in its dedicated window. Closing the window leaves services running; `Stop-Invora.cmd` stops services while retaining the database and uploaded files. A normal computer restart leaves the volumes intact; start Docker Desktop and the launcher again.

The executable installer can update the existing folder as well: take a verified backup first, then run the new `InvoraSetup.exe`, checking that it shows the original folder. It checks both running and stopped Invora containers when finding the original installation. It preserves the existing `.env`, rebuilds images and never removes database/document volumes. For an explicitly chosen directory, run `InvoraSetup.exe --install-dir "C:\Invora"` from PowerShell. Moving/recovering a shop database to another PC is a separate backup/restore operation, not a fresh install with a new `.env`.

For an update, first take a verified database **and document** backup. Keep the existing `.env` and Docker volumes. Extract the new release over the application files without replacing private configuration, then run in PowerShell from `C:\Invora`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-Invora.ps1 -Update
```

`-Update` explicitly rebuilds images from that release. The execution policy option applies only to this launcher process; it does not change the machine's persistent policy. Review the script before running it. Migrations run separately before API/web startup and fail rather than deleting or resetting existing data. A release with an incompatible migration requires a reviewed deployment/recovery decision; do not treat restoring an old database as an ordinary code update.

## Records and backup

Business records live in **`invora_database`** and uploaded private bills in **`invora_documents`**, under Docker Desktop's managed storage. They do not live in the extracted ZIP. Keep `.env` private; preserve it separately for recovery. Removing/resetting Docker or running volume-deletion commands can erase your records. The launcher never removes those volumes.

Use the project's encrypted backup/restore scripts from a Linux/WSL shell with Docker integration and the configured RSA keys, as described in [deployment and recovery](deployment.md). Stop API/web during the backup window so the database and document archive agree; leave the database running. Store backups off the shop PC, retain multiple versions, and test recovery. Docker's [volume/VM backup documentation](https://docs.docker.com/desktop/settings-and-maintenance/backup-and-restore/) is useful for host recovery but does not replace the application's verified database-and-documents backup process.

The optional [offsite backup command](offsite-backups.md) pauses/resumes API/web and transfers an encrypted archive to a configured second drive and/or rclone cloud bucket. On Windows, run it in WSL with Docker Desktop integration; verify that WSL can see the external drive and remote configuration before scheduling it.

## Troubleshooting

Double-click **Check Invora**, or `Check-Invora.cmd` in the application folder. It shows PASS/WARN/FAIL with next steps and saves `artifacts\invora-check-<UTC timestamp>.json`. The checks inspect required files, the local address, Docker, existing data volumes, Compose configuration, running services and application readiness. They do not start/stop containers, migrate, generate credentials or change shop records. Only the diagnostic report is written.

The report uses predefined messages and excludes secrets, licence keys, raw Docker output/logs, customer records and Windows user/machine names. Review that report before sharing it with support; do not send `.env` or `docker compose config` output. A passing report confirms startup readiness, not backup integrity or financial reconciliation. A new installation can show warnings until the first successful start.

If installer startup fails, its window stays open so you can read the error. Correct the cause and use **Start Invora** again. For a failed build/update, rerun the same installer so it rebuilds the release. Keep the application folder and its private configuration.

| Situation | What to check |
|---|---|
| Docker not ready | Start Docker Desktop; wait for the engine and confirm Linux containers |
| First build fails | Check internet, disk space and the build error; retry without deleting configuration or volumes |
| Port 8080 is occupied | Stop the conflicting app, or change both `INVORA_HTTP_PORT` and `INVORA_BROWSER_ORIGIN` in `.env` consistently |
| Local address check fails | Keep one numeric port entry (1–65535) and set the browser origin to `http://127.0.0.1:<port>`; remove conflicting Windows environment overrides |
| Existing data but no configuration | Use the original install folder or recover its private `.env`; do not create replacement passwords for existing volumes |
| App isn't ready | Run `docker compose ps` and `docker compose logs api web`; readiness is `/health/ready` |
| Setup is closed | Your owner is already registered; use the existing login instead of resetting the database |
| Migration fails | Preserve the error and backup; correct the cause before proceeding |

Use the [owner flow and security guide](owner-guide.md) to understand ledger direction, GST and storage. For multiple computers, phones or outside access, deploy the existing HTTPS server configuration and use its domain from each browser; this ZIP does not configure that hosting automatically.

The Windows x64 executable is cross-published and its embedded archive/hash/path checks are executed on this development host. PowerShell logic is exercised with isolated Docker/HTTP doubles on macOS. Native Windows installation, shortcut/ACL behavior, Docker Desktop startup and store scanner/printer acceptance still need verification on the target Windows PC. Record those results using [Windows acceptance](windows-acceptance.md); do not describe them as completed before running them.

See [desktop windows and local addresses](desktop-app.md) for how the host and shared server options work. Native WebView2 rendering and printer acceptance require testing on a Windows PC; cross-publishing is not Windows runtime verification.
