# Selling and licensing Invora

## What the shop receives

Deliver `InvoraSetup.exe`, the [Windows instructions](windows-installation.md), your support/contact details and the licence file issued for that shop. Install Docker Desktop first, run the installer, create the owner's login, and open **Shop licence** to copy the Shop ID. After you collect the agreed payment, issue a licence for that ID. The owner pastes the `INVORA1…` key at `/license`. No owner password, database password or setup key is sent to you for licensing.

The installer contains the application release and bundled .NET installer runtime. It installs application files and Start/Stop/Check shortcuts, then builds/starts the Linux containers. **Check Invora** produces a report without collecting configuration values or shop records, for owner-led support. Docker Desktop, virtualization/WSL, internet for first build and a suitable Windows x64 PC remain prerequisites. It is not a Docker-free desktop database executable. The Windows executable is cross-published from .NET; see Microsoft's [single-file deployment documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview). Complete the [native Windows acceptance record](windows-acceptance.md) and sign the final executable with your own trusted Windows code-signing certificate before public distribution; no certificate or signature is fabricated here.

For Mac customers, deliver **`Invora-Mac.dmg`** (or the ZIP), its SHA-256 sidecar and the [Mac installation guide](macos-installation.md). The native universal app requires Docker Desktop and uses the same signed Shop ID licence flow. Its current ad-hoc signature is for local integrity checking; obtain Developer ID signing and Apple notarization before broad public distribution. No database, backup, private `.env` or vendor signing key is included.

## Which key is private?

| Item | Who uses it | Purpose |
|---|---|---|
| `INVORA_BOOTSTRAP_KEY` in each shop's `.env` | Shop owner, once | Create the first owner and business; **not a paid licence** |
| Owner password | Owner only | Sign in to that shop |
| Vendor private signing key | You only | Issue authentic shop licences; never distribute it |
| Public verification key | Customer application | Check licence signatures; safe to distribute |
| `INVORA1…` licence key / `.license` file | Named shop owner | Activate or renew that Shop ID through the paid date |

This workspace's private signing key is `.vendor-private/license-private.pem` (ignored, mode 0600 on this host). Its public key is in `deployment/license-policy.json`. Neither the private key nor issued licence files are included in the release ZIP, Docker build or installer. Keep an encrypted offline backup of your signing key before selling licences. Do not regenerate or replace it during ordinary updates: existing licences depend on that verification key. Changing vendors/keys requires a reviewed key-rotation migration.

## Issue a paid licence

From the vendor's source workspace with .NET 10:

```sh
dotnet run --project tools/Invora.LicenseTool -- issue \
  --private .vendor-private/license-private.pem \
  --shop-id SHOP-ID-FROM-THE-OWNER \
  --customer "Customer shop name" \
  --starts 2026-10-07 --expires 2027-10-06 \
  --grace-days 7 --output .vendor-private/issued/shop-2027.license
```

Replace the ID, dates, customer and output filename. Deliver only the resulting `.license` file to that shop. Renewal uses the same Shop ID with a later expiry and a new filename. The tool refuses to overwrite an existing private key/policy/licence file. To initialize a separate vendor installation, run `keygen --private YOUR-PRIVATE-FOLDER/key.pem --policy deployment/license-policy.json` once, using the same tool; it will refuse to replace an existing policy.

Licences use RSA-PSS/SHA-256 with a 3072-bit signing key, signed shop ID, Retail plan, paid start/expiry dates and a bounded grace period. The server verifies the signature and shop ID. Activation is owner-only and append-only; retries do not create duplicate activations. A shorter renewal cannot replace a later licence. Validity uses UTC dates. During grace, transactions continue and a renewal banner appears. After grace, new business writes are blocked while login, records, PDFs, reports, CSV exports, renewal and host backups remain available. Renewing re-enables writes without replacing the database.

Fresh Windows and Mac releases load the public commercial policy into newly generated `.env` files. Existing `.env` files and this current developer installation remain unchanged. To explicitly enable licensing on a previously unlicensed customer deployment, configure `INVORA_LICENSE_REQUIRED=true` and set `INVORA_LICENSE_PUBLIC_KEY` to the policy's public key, then restart API/web. Agree the change with that customer first. Never use the vendor signing key in the shop's environment.

Offline local licensing is a practical entitlement check, not tamper-proof DRM against someone who administers the PC, modifies the application/configuration or changes its clock. A Shop ID identifies a database, not a physically unique PC; a copied database retains that ID. Strong revocation/device limits require an authenticated licence server with a documented offline policy. This release does not claim online revocation, device binding, automatic payment collection or protection against a privileged database administrator.

## A manageable income model

Start with one clearly priced **annual Retail licence**, plus an optional one-time installation/opening-stock service. Define whether updates, support, on-site visits and backup assistance are included. Charge for the promised service, retain your customer's data ownership/export access, and keep expiry/renewal dates clear. Offer a time-limited signed pilot licence before asking a shop to depend on the product.

An example to validate with shop owners is ₹6,000/year plus ₹2,000 initial setup; this is a proposed offer, not a market-price claim. Ten renewed shops would produce ₹60,000 annual licence revenue before tax, support, hosting, travel and other costs. Keep a vendor register of Shop ID, customer contact, agreed price, receipt/reference, issued key ID, expiry and support history. Record customer payment in your own business accounts, then issue the key. Compare your offer against current published alternatives such as [Vyapar pricing](https://vyaparapp.in/pricing) and [Zoho Books India](https://www.zoho.com/in/books/pricing/) before fixing it.

This application now provides installation, signed activation and renewal. Customer acquisition, collecting licence payments, ongoing support and an operational backup service are business work; revenue is not generated automatically by the executable.

## Build the customer release

```sh
python3 scripts/package-windows.py
dotnet publish tools/Invora.Setup -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -o artifacts/windows-installer
```

Copy `artifacts/windows-installer/InvoraSetup.exe` to `artifacts/InvoraSetup.exe` for delivery. Repackage before publishing so the embedded release contains the final application and public policy. `--verify-package` validates the embedded archive; it does not install anything. The installer checks every source file hash and path before extracting, rejects private `.env`/licence files and linked destinations, preserves an existing `.env`, and never removes Docker volumes. File hashes detect corruption; they do not replace executable code signing.

For Mac, build on macOS with `python3 scripts/package-macos.py`; see the Mac guide for build prerequisites and remaining platform acceptance. Never embed customer data or private vendor keys when rebuilding either platform release.
