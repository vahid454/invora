# Where Invora runs

Invora has one application and one PostgreSQL database per shop. The desktop window is a client; it is not the database. Choose the mode before onboarding a shop, because installing a second independent database does not synchronize records.

For the current offline shop, use **local Docker + PostgreSQL + encrypted Backblaze backups**. Hosted mode is an optional separate deployment for shops that need the same live records on multiple devices and can rely on internet access.

| Mode | Customer computer | Where live records reside | Internet for billing | Current readiness |
|---|---|---|---|---|
| Hosted shop | Invora desktop window or browser; no Docker | Shop's isolated PostgreSQL and documents on the configured HTTPS server | Yes | Desktop connection is implemented; a production server, domain, TLS, backup and monitoring must be provisioned per shop |
| Local shop | Invora desktop window plus Docker Desktop | PostgreSQL and documents in Docker volumes on the shop computer | No, after initial setup | Existing Mac/Windows installer and backup workflow |
| Native offline shop without Docker | Future installer bundling PostgreSQL, .NET service and web assets | Private application data folder on the shop computer | No, after initial setup | **Not released**; requires installer, service lifecycle, updates, backup/restore and hardware acceptance |

## Hosted shop without Docker on customer computers

Provision the server using [production deployment](deployment.md). Give the shop a unique HTTPS origin such as `https://shop.example.com/`, configure the API's `Auth__BrowserOrigin` to that exact origin, and complete TLS, database isolation, migrations, encrypted offsite backups, monitoring and a restore rehearsal before live billing. This repository does not create those cloud resources automatically. Backblaze B2 is a backup destination, not a live application/database host.

On **Mac**, install Invora.app and choose **Connect to hosted shop…**; paste the shop's root HTTPS address. This path does not call Docker. On **Windows**, run the current desktop executable directly and enter that HTTPS address at first launch. It remembers the address in `%LOCALAPPDATA%\Invora Desktop\hosted-url.txt`; run `Invora.Desktop.exe --configure-hosted` to change it. A vendor can provision a shortcut with `--hosted-url https://shop.example.com/`. Do not run the Docker-based `InvoraSetup.exe` for a hosted-only customer. The Windows desktop executable still needs the free WebView2 Runtime.

The URL is only a pointer, not a password or copy of shop records. Mac stores it in app preferences; Windows stores it in the user's local app data. Staff sign in with their own account. Browser/WebView session data is local to each device, but sales, stock, invoices, customer accounts and uploaded documents stay on the shop server. A second Mac, Windows PC or browser reaches the **same** records by using the same HTTPS origin and authorized account. If the internet or server is down, hosted billing pauses; no local offline transaction queue exists.

## Local shop and the Docker-free offline target

Today the local launcher uses Docker to run PostgreSQL, the .NET API and nginx. Docker is a deployment choice, not a dependency of the business logic. A native offline installer can replace it, but it must ship a compatible PostgreSQL server, a self-contained .NET API, built Angular assets and a supervised background service. It must initialize a private data directory, restrict permissions, start and health-check services, apply migrations explicitly, handle ports and process shutdown, preserve data during updates and uninstall, and keep encrypted versioned backups off the device. Windows and Mac require separate signed installers and recovery tests. Do not sell the current local package as Docker-free or copy only the desktop app expecting records to follow.

For either local implementation, switching devices requires a verified database **and uploads** backup, the matching private configuration and decryption key, and a rehearsed restore on the new device. The existing [offsite backup policy](offsite-backups.md) protects the currently configured Mac only; it is not automatically installed for each customer. Never treat a single disk or a single cloud bucket as a guarantee that data cannot be lost.
