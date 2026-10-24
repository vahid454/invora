# Install and run Invora on a Mac

The Mac release is **`Invora-Mac.dmg`**, containing a native universal **Invora.app** launcher for Apple silicon and Intel Macs. A ZIP of the same app is available as `Invora-Mac.zip`. Billing opens in a dedicated Invora window using macOS WebKit. For a separately provisioned hosted HTTPS shop, choose **Connect to hosted shop…** and enter the shop address; Docker is not needed on this Mac. For local offline billing, the launcher starts .NET 10, Angular and PostgreSQL through Docker Desktop. See [deployment modes](deployment-modes.md). No Python, Node or .NET installation is needed on the owner's Mac.

## Hosted shop (no Docker on the Mac)

Install Invora.app from the DMG, open it, choose **Connect to hosted shop…**, enter the shop's root HTTPS address and sign in. This path does not start Docker or install a local database. The Mac remembers the address for the next connection. The production server holds business records and uploads and must have its own tested backups; internet is required for billing. The app can also open a local shop later, but the two databases do not synchronize.

## First installation

1. For a **local** shop, install and start [Docker Desktop for Mac](https://docs.docker.com/desktop/setup/install/mac-install/), choosing Apple silicon or Intel for that Mac. The launcher targets macOS 14 or newer; Docker's current supported macOS versions also apply. Review Docker Desktop's licence terms for your organization.
2. Open **Invora-Mac.dmg**, drag **Invora.app** to **Applications**, and open it from there. Eject the installer after copying. Alternatively, unzip `Invora-Mac.zip` and move its app to Applications.
3. Choose **Start shop**. The first run verifies the bundled files, creates a private configuration, downloads/builds containers, starts the database, applies explicit migrations, and opens the Invora shop window when ready. Keep Docker running. Initial installation requires internet and can take several minutes; normal local billing uses the built images.
4. A new installation opens the local shop in its own window, without an address bar. Use **Set up Invora** on the sign-in page (or **Open in browser** and visit `http://127.0.0.1:8080/setup`). In the launcher choose **Application folder**; press **Shift–Command–.** in Finder to show hidden files. Read `INVORA_BOOTSTRAP_KEY` from the private `.env` using a text editor and use it to create the business and owner login. There is no preset owner password. Keep the key and `.env` private. Existing shops sign in with their existing accounts.
5. Commercial releases require the vendor's signed key in **Shop licence**, issued for the Shop ID shown there. The setup key creates the owner; it is not a paid licence. Existing licensing configuration is preserved.
6. Complete business/GST settings, individual employee accounts and permissions. Verify your actual scanner, printer and backup process before normal shop use.

The current app has an **ad-hoc signature**, which supports local integrity checking but is **not an Apple Developer ID signature or Apple notarization**. Verify the vendor and the separately supplied SHA-256 checksum. If macOS blocks this trusted build, Apple's [per-app approval instructions](https://support.apple.com/en-au/102445) explain Privacy & Security → Open Anyway. Do not disable Gatekeeper globally. Public commercial delivery should use the vendor's Developer ID signing and notarization.

## Daily use

| Button | Result |
|---|---|
| Start shop | Opens an already healthy shop, or starts its containers and migrations |
| Connect to hosted shop… | Opens a configured HTTPS shop; does not call Docker |
| Check status | Checks local Docker, configuration, data-volume presence and readiness; does not alter services or records |
| Stop shop | Stops only the Invora Compose services; retains records and uploads |
| Update release… | Installs the bundled source release, rebuilds and applies migrations after pausing billing |
| Application folder | Opens the selected installation folder, including its hidden private `.env` |
| Installation guide | Opens the bundled offline instructions |

Closing the shop window returns to the launcher. Closing the app leaves the services running. Use Stop shop to stop them. The shop toolbar includes Back, Reload, Downloads and Open in browser; PDF/CSV downloads ask where to save. The Mac must be awake and Docker running to serve billing. After restarting the Mac, start Docker Desktop and then Invora. The launcher does not install a background macOS service or configure remote access.

## Installation and data folders

New application/configuration files go to `~/Library/Application Support/Invora`. The launcher remembers the path in `~/Library/Application Support/Invora Launcher/installation.txt`. Existing installations are detected using **running and stopped** Invora containers; the original folder and `.env` are reused. This workspace therefore continues using its existing repository folder.

Business records are in Docker volume **`invora_database`**. Uploaded files are in **`invora_documents`**. They live in Docker Desktop's managed Linux VM storage, not inside Invora.app or the installation folder. Copying the app to another Mac does **not** transfer records. Removing the app does not erase the volumes; resetting Docker or deleting its data can. See [data storage and security](data-storage-security.md).

Only one local Invora business installation is selected by this launcher. Multiple branches belong to that one database. A Mac and a Windows PC can use one shared shop through the separately configured HTTPS server deployment; installing independent local apps creates independent databases, with no automatic synchronization.

## Updates and recovery

Take and verify a database-and-uploads backup first. Replace Invora.app in Applications with the new release, open it, and choose **Update release…**. The launcher preserves `.env` and data volumes. A failed build is marked for retry before the new files can start. A failed migration leaves billing stopped, with an error to investigate; it never deletes data to force an upgrade through.

If existing volumes are found without the original `.env`, startup stops. Recover the original configuration and folder instead of creating replacement database passwords. The installation pointer may be corrected by support when no existing Invora container identifies the original folder. Do not change this pointer to create a second shop against the same volumes.

For this local Mac, the configured 9 PM job saves a dated encrypted backup to `~/Library/Application Support/Invora Backups` outside Docker and pushes it to private Backblaze B2. The app has **Backup folder**, **Check backup** and **Prepare restore…** controls. Open `Enable-Invora-Recovery.command` once and save the recovery password away from this Mac to activate the single-file `.invora` packages; without that step, the older `.backup` archives still require separate recovery keys and `.env`. The app can verify and unpack one file on another Mac, but replacing the live database requires an isolated restore and reconciliation. See [local and cloud backups](offsite-backups.md) for exact limits and recovery steps.

For a command that pauses and resumes API/web around the encrypted snapshot and then copies it to a configured second drive and/or cloud bucket, see [offsite backups](offsite-backups.md). A fresh installation still needs a shop-owned destination and a scheduled job; this development Mac now has a configured private Backblaze bucket and daily 9 PM LaunchAgent. That schedule is local to this Mac and does not transfer with Invora.app.

## Troubleshooting

| Message or problem | Next step |
|---|---|
| Docker not ready | Start Docker Desktop and wait for the Linux engine |
| Remote Docker context | Select your local Docker Desktop context; the launcher refuses remote engines |
| Existing data, missing `.env` | Recover the original installation folder/configuration |
| Port/origin mismatch | Keep a numeric `INVORA_HTTP_PORT` and matching `http://127.0.0.1:<port>` or `http://localhost:<port>` origin in `.env` |
| Build failed | Check internet/disk space; run `docker compose build` from the application folder for details; retry Update release |
| Migration failed | Keep backups; inspect `docker compose run --rm migrate` with support before starting billing |
| Shop not ready | Check status, then inspect `docker compose logs api web` privately |
| macOS blocks opening | Verify provenance and checksum; follow Apple's per-app approval guidance above |

Check status displays predefined messages, not credentials or customer records. Detailed Docker logs can contain private operational information: review before sharing. Passing readiness is not proof of backup integrity or financial reconciliation.

## Build and acceptance

On a development Mac with Python 3 and working Xcode Command Line Tools:

```sh
python3 scripts/package-macos.py
```

If the selected Xcode installation is incomplete but Command Line Tools are installed, use a command-scoped override:

```sh
DEVELOPER_DIR=/Library/Developer/CommandLineTools python3 scripts/package-macos.py
```

The build compiles arm64 and x86_64 slices, combines them, verifies the release manifest and ad-hoc signature, and creates DMG/ZIP files plus SHA-256 sidecars in `artifacts`. It excludes private configuration, vendor signing keys, issued licences, backups, generated files and business records.

Read-only diagnostics and startup against the existing Apple silicon installation are tested locally. Intel execution, a new owner's first installation, downloaded/quarantined Gatekeeper behavior and real scanner/printer acceptance require their own target-Mac checks. Cross-compilation is not an Intel runtime test. Public signing/notarization remains a vendor release step.

See [dedicated desktop windows](desktop-app.md) for the internal local address, browser fallback and session storage.
