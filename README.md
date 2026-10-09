# Invora

Inventory. Billing. Business. Simplified.

A retail workspace for mobile/electronics stores: **one business per PostgreSQL database, multiple branches**. .NET 10 API, Angular 22 UI, PostgreSQL 18, and free MIT-licensed PDFsharp/MigraDoc invoice generation.

The combined Phase 1/2 application includes catalog and taxes, serialized/quantity stock, purchasing, scanner billing, customer/supplier accounts, payments and advances, returns/cancellations/refunds, LenDen, transfers, used devices/exchanges, expenses, reports, reviewed CSV imports/exports, private documents, staff access, and audit history. Posted corrections append linked records; financial and inventory posting is transactional and idempotent.

Start with [product rules and correction specification](spec.md). See [implementation status](docs/implementation-status.md), [architecture](docs/design-review.md), [authentication](docs/authentication.md), and [deployment/recovery](docs/deployment.md).

For shop operators, read [daily flow, ledger direction, data storage and security](docs/owner-guide.md). For Windows, use `InvoraSetup.exe` and the [installation guide](docs/windows-installation.md); Docker Desktop is required. The source ZIP/CMD launchers remain available. See [commercial installation, paid licences and renewal](docs/commercial-release.md) for selling the app and keeping vendor signing keys private.

For Mac, use **`Invora-Mac.dmg`** with its native **Invora.app** launcher; see [Mac installation](docs/macos-installation.md). It supports Apple silicon and Intel builds and requires Docker Desktop. Read [where data is stored, security limits and backups](docs/data-storage-security.md) before installing a customer shop.

Windows releases include **Check Invora** for read-only startup diagnostics and a report without secret values or shop records. Target-PC installation and hardware results belong in the [Windows acceptance record](docs/windows-acceptance.md).

## Run the complete application

Install Docker Desktop and Python 3, then run:

```sh
python3 scripts/dev.py
```

This generates a private `.env` if none exists, builds both containers, starts PostgreSQL, explicitly applies migrations, and serves the application at **http://127.0.0.1:8080**. Open `/setup` and use `INVORA_BOOTSTRAP_KEY` from your private `.env` to create your business and owner account. There is no default owner password or automatic production seed. Bootstrap closes after the first successful setup.

The development stack binds the web port to localhost. Keep `.env` private. Stop it with `docker compose stop`; database and document volumes persist. Do not remove volumes containing your records.

## Develop and verify

Pinned tools: .NET SDK **10.0.401**, Node **24.21.0**, Angular **22.2.1**. Docker is required for PostgreSQL integration tests.

```sh
dotnet restore Invora.sln --locked-mode
dotnet build Invora.sln --no-restore
dotnet test Invora.sln --no-build
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/Invora.Infrastructure
cd invora-web
npm ci
npm test
npm run build
```

For independent API/UI development, configure the connection and auth environment described in [authentication](docs/authentication.md), run the API at `http://127.0.0.1:5080`, set `Auth__BrowserOrigin=http://127.0.0.1:4200`, and run `npm start` inside `invora-web`. The Angular development proxy forwards API requests to port 5080. Apply migrations explicitly before starting the API.

Browser acceptance uses Playwright against an **isolated development database**. See [testing](docs/testing.md) for its setup and hardware acceptance checklist. CI builds and tests the API, UI, migrations and container images.

Cloud rollout remains environment-dependent: AWS account/region, domain, TLS certificates and off-host backup destination must be configured. No AWS resources have been created. Repairs/finance-provider workflows, automated messaging integrations, subscription billing and AI/OCR are the separately described Phase 3/4 extensions.

Counter workflows include per-phone color/memory/identity entry, old-phone exchanges, two customer phone numbers, payment promises and WhatsApp/SMS/call actions, shareable account PDFs, clear advance/refund choices and guided branch transfers.

For daily use, see [counter workflows](docs/counter-workflows.md).

Invoices can be found by customer name, either phone number, tax-invoice number or shop reference. Dated shop references such as `INV-SP-MAIN-07Oct26-000001` accompany a shorter GST invoice number; the PDF records the actual issue time and place of supply. Inventory filters include brand and stock status. Account screens distinguish **Lena hai** (receive) from **Dena hai** (pay), including supplier credits and separate sales/independent LenDen balances.

Create a Windows release with `python3 scripts/package-windows.py`. It packages application source, locked dependencies, launchers and documentation into `artifacts/Invora-Windows-local.zip`, with file hashes, excluding private configuration, generated files and business records.
# invora
