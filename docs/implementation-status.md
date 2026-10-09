# Implementation status

Updated 8 October 2026. This replaces the earlier foundation-only status.

## Approved scope and completed software

8 October Mac delivery adds **Invora.app**, packaged as a universal Mac DMG/ZIP, with start/status/stop/update controls, private first-install configuration, reuse of existing installations and verified payload hashes. Docker Desktop remains required. See [Mac installation](macos-installation.md), [data storage/security](data-storage-security.md) and the native-launcher verification scope in [testing](testing.md). The app currently has an ad-hoc signature; vendor Developer ID signing/notarization and Intel/target-shop acceptance remain release steps.

The owner explicitly requested clearing the local operational data while retaining staff, shop settings and catalog. A verified recovery snapshot was created first, the reset was rehearsed on a restored database, and the live cleanup passed empty-table/document, protected-hash and reconciliation checks. Existing passwords/configuration, licences, audit history and numbering were retained; sessions were revoked. This maintenance reset is documented in the storage guide and is not part of the installer or ordinary updates.

.NET 10, current pinned stable toolchains, one business per database with multiple branches, combined Phase 1/2 delivery, and correct inventory/money corrections. The supplied photograph guides the commercial invoice layout; its personal/bank data and official QR/IRN are not copied into production defaults.

| Milestone | Current result |
|---|---|
| M0–M2: architecture, foundation, access | Seven-project solution, explicit PostgreSQL migrations, protected single-business setup, staff grants/templates, branch membership, rotating sessions, owner protections, ProblemDetails, health and audit |
| M3: catalog, stock, scanner | Configurable percentage GST/cess, variants/SKU/barcode, permanently unique cross-slot IMEIs, serial identities, quantity balances and exact FIFO cost layers; scoped HID/manual scanner queue |
| M4: purchasing | Draft/review/post, serialized capture counts, exact inventory valuation, supplier payable, financial-year supplier invoice uniqueness, private bill attachments, supplier returns |
| M5: sales and LenDen | Server total review, transactional sale and mixed payments, credit limits/due dates, later payments/advances, allocation/reversal, customer and supplier ledgers, LenDen, cancellation/partial returns/credit notes/refunds |
| M6: invoices | Immutable seller/buyer/item/identity/tax snapshots, commercial A4 invoice and credit-note PDFs, Indian amount words, repeating multipage table headers/page numbers; immutable payment receipt PDFs |
| M7: expense/dashboard/audit | Immutable expense reversals, posted date-based business metrics, authorized cost/profit views, branch-scoped audit, CSV exports and reconciliation SQL |
| M8: Angular application | Responsive workspace, login/setup, branch switch, scanner sales/purchases, inventory/aging/history, directories and editing, account details/notes, documents, LenDen/payments, returns, partial transfer receipt/cancellation, used acquisition and atomic exchange, settings, staff templates/grants, reports, reviewed imports |
| Combined Phase 2 | Supplier ledger, partial returns, partial transfers, stock aging, used-device inspections/reacquisition/exchange, branch comparisons, reviewed catalog/customer/supplier/opening-stock CSV imports and authorized exports |
| M9: automated acceptance | Unit and real PostgreSQL integration regressions plus actual Chromium workflows. Physical scanner/store pilot acceptance remains external |
| M10: deployment/recovery | Verified API/web containers and migration bundle, Compose, restricted runtime DB role, TLS overlay, CI, encrypted/signed backup and isolated restore/reconciliation rehearsal. Live AWS/DNS/TLS/off-host backup rollout remains external |

## LenDen search, invoice dates and sale payments

The oversized LenDen customer input came from a horizontal toolbar flex rule applying inside a vertical label. That rule now targets direct toolbar children; customer search and account filters are aligned, bounded controls with a stacked mobile layout.

Sales & invoices adds Today, Yesterday, Last 7 days, This month and custom From/To filters. The API filters the invoice business date inclusively before pagination and combines it with customer/phone/invoice search. The sales CSV uses the same search and dates. Invalid ranges are rejected in the API and explained beside the controls.

New-sale payment entry normalizes cleared cash/UPI/card fields to zero and rejects negative, invalid or fractional-paise values instead of dropping them. The summary shows each method, total received, exchange credit and the remaining invoice due. Payment edits retain the reviewed bill value for an immediate preview and require a fresh review before completion. Full payments do not require a credit due date; partial payments leave only the unpaid amount in LenDen. Existing atomic payment posting/allocation and retry behavior remain intact; no schema change is required.

Verification on 8 October: 33 backend unit tests, 66 PostgreSQL integration tests, four TypeScript/scanner tests and three targeted Chromium workflows passed. The workflows cover 375px layouts, inclusive/preset date filtering and CSV, cleared/invalid payments, exact split payment, full/partial settlement and LenDen. The earlier full counter suite remains documented separately in [testing](testing.md).

## Counter, accounts and follow-up enhancements

### Final warranty, reminders and inventory corrections

The agreed behavior is recorded in root `spec.md`. New sale snapshots use the service-partner/service-centre warranty policy; used customer devices show no warranty, and used acquisitions/exchanges store zero warranty days. Earlier invoice snapshots retain their original terms. Credit notes carry the original condition/warranty text.

Inventory and Used devices now have a guarded device-details dialog. Authorized employees can correct one available device's colour, RAM/storage, battery health and inspection notes with a reason and a last-seen revision. Shared variants, protected identities, purchase cost, quantity and posted financial history stay protected. Corrections append before/after history; inspections remain immutable. The additive `DeviceDetailCorrections` migration creates only the correction-history table/indexes/trigger and extends SELECT/INSERT grants for an existing runtime role.

Customer reminder drafts use paragraphs, shop name/contact, current promises and the requested “payment defaulter” wording scoped to shop records. Manual draft edits survive account/promise refreshes. Used-device rows now identify the actual model/IMEI or shop tag and expose the correction dialog. Dialogs include loading/error states, readable history and mobile controls. Final verification passed 33 unit, 63 PostgreSQL integration, two scanner and sixteen counter browser checks; the phone and used-device PDF samples remain one page.

### Final payment polish and commercial Windows delivery

Invoice amount words and tax summaries have explicit separation, wider tax-rate columns and cleaner seller/contact headings. The eight-phone sample remains one A4 page and the long fixture retains repeated multipage headers. Payment entry accepts the full actual amount with an optional selected invoice: automatic mode settles oldest branch dues; a selected bill receives only its remaining due; excess stays on the trading account. Explicit advances keep all money unallocated. The preview explains the split, the API calculates final allocations inside the posting transaction, and existing refund/credit/idempotency protections remain enforced.

Inventory CSVs now match devices, quantity, aging or movement views and applicable search/brand/category/status filters. They include dated filenames, shop-time-zone receipt/movement/export dates, separate IMEI/serial/specification columns, readable headings and authorized costs. Empty results preserve headings. Movement search now works across movement notes/types, product/SKU and device identities. UI polish improves control sizing, contrast, filter spacing, action alignment, compact payment contacts, focus indicators, empty-search reset and narrow-screen layouts.

`InvoraSetup.exe` is a cross-published self-contained Windows x64 installer with the release embedded, verified archive hashes/paths, per-user installation, desktop shortcuts and a Docker startup path. It preserves existing configuration/volumes, remembers the installed folder, and rejects creating new credentials for existing volumes without the original configuration. Docker Desktop remains required. The executable is unsigned; actual Windows installation/ACL/shortcuts and hardware need target-machine acceptance before public delivery.

The installer now also finds stopped installations, drains Docker output safely with a timeout, and keeps startup failures visible. Start/Check validate the local port and matching browser origin before startup mutations. A **Check Invora** shortcut provides read-only readiness checks and a timestamped support report without credentials, logs or shop records. Fourteen isolated PowerShell/Docker/HTTP checks pass on macOS; [native Windows acceptance](windows-acceptance.md) remains explicitly pending.

Commercial licences use signed shop-ID entitlements, owner-only activation/renewal, UTC expiry, grace and read-only access after expiry. Records, exports, login, PDFs and renewal remain accessible. The additive `ShopLicensing` migration creates `BusinessLicense` for immutable activations and grants existing restricted runtime roles only SELECT/INSERT. Current local licensing remains disabled. Private vendor keys stay in the ignored `.vendor-private` directory and are excluded from ZIP/container/installer. Offline licensing is not administrator-proof or device-bound; automatic licence payment collection and online revocation are not implemented. See [commercial release](commercial-release.md) for issuance, pricing examples, boundaries and customer delivery.

### Invoice lookup, GST and account clarity

Invoices now expose customer name, both phone numbers, business date, legal invoice number and dated shop reference. Search accepts multiword names and formatted primary/alternate numbers. New shop references follow `INV-SP-MAIN-07Oct26-000001`; the corresponding GST number is the shorter `I-07Oct26-000001`. Sequences are shared across branches and unique within a financial year, with a date and separately recorded issue timestamp. Posted historical numbers and snapshots are preserved. See the [owner guide](owner-guide.md) for the GST 16-character limit and place-of-supply guidance.

The sale calculates inter/intra-state GST from the selected delivery state, falling back to the customer or shop state, rather than rejecting a stale manual tax-mode checkbox. State names/codes and GSTIN prefixes are normalized and checked. The traditional A4 invoice groups matching phones while retaining every IMEI, HSN, taxable amount, GST summary, amount words, bank details and signatory area; the eight-phone sample fits one page. Longer invoices still repeat headers across pages. Credit notes link their original invoice. No official IRN/QR is fabricated.

New serialized products capture colour/RAM/storage only in each phone's identity row; quantity products and existing variant editing retain specification fields. Inventory has brand/status filters. Customer and supplier screens express amounts as **Lena hai / Dena hai**, separate trading and independent LenDen ledgers, and expose supplier credits/refunds without misleading minus signs. Dashboard receivables/payables do not hide dues through opposing independent balances. Promise reminders include the latest promised date, even beyond the current history page, with editable WhatsApp/SMS drafts. They do not make unsupported bank-default claims.

The additive `InvoiceSupplyAndAccountClarity` migration adds only the sale reference column. Existing stock, money, customers, documents and invoice history are preserved. A Windows source ZIP, double-click Docker launchers and [installation guide](windows-installation.md) are available alongside the [data/security and owner flow guide](owner-guide.md). Native execution of the launcher on Windows remains a target-machine check.

Product entry and purchase receiving capture each physical phone's own IMEI, RAM, storage, color, cost and selling price. Purchase preparation groups matching variants/costs without posting inventory or a supplier balance; completion posts the actual bill. Extra device rows can be removed immediately, with quantity kept in sync. Buttons and device fields use consistent spacing and touch sizes.

Sale exchange describes the customer's old phone separately from the device being sold. A known catalog variant is optional; enter the old device name/specifications instead. IMEI/serial and inspection details are optional for used acquisitions. Without a physical identifier, a unique `SHOP` tracking tag identifies the received stock without fabricating an IMEI. Optional seller name and Aadhaar last four are recorded with inspection history; full Aadhaar values are rejected. Malformed sale-review JSON now returns a useful input error instead of a server failure.

Customers can have two searchable mobile numbers. LenDen adds branch-scoped, append-only payment promises, promised amounts, next-contact dates and conversation/reminder outcomes. Filters cover trading dues, independent dues, account credit, promises and follow-ups. A credit in one account cannot hide debt in the other. WhatsApp/SMS links open editable drafts, calls use the selected phone number, and authorized customer statement PDFs can be downloaded or shared. Contact actions require the operator to send the message; no scheduled/provider messaging is claimed.

Payment purpose explicitly distinguishes invoice settlement, advance and returning account credit. Invoice choices show the selected party's actual remaining dues across its history. Refunds retain the existing trading-credit limit and do not allocate invented invoices or consume independent LenDen credit.

Stock dispatch has a source/destination branch guide, inline receiving-branch creation for authorized staff, searchable/scannable phones and quantity accessories, a removable dispatch list and quantity controls. Branch creation leaves the source scanner usable; dispatch waits for pending scans. Arrival requires an explicit switch to the destination and counted receipt. Device identity and exact costs survive the handoff. Reports add date presets, daily net-sales/return bars, net top products, current stock aging/transit counts and detailed CSV exports. Returns of earlier-period sales remain visible in the selected return period.

`ShopCounterEnhancements` adds contact/inspection columns and follow-up history without replacing existing records. Its restricted-runtime grants and immutable history trigger are verified, and the optional Aadhaar reference has a database constraint limiting it to four digits.

## Counter usability update

Sales and purchases now search contacts by phone/name and create/select contacts inline. A reusable product form handles mobile/electronics fields, inline tax setup and atomic product plus opening inventory. Serialized products can be selected from available branch devices without a scanner. LenDen has separate customer account balances and independent money-given/received entries, without fabricating invoices. Co-owner/manager and trusted staff templates are available. See [counter workflows](counter-workflows.md). Existing records and staff grants are preserved.

## Mobile variants and shop organization

Mobile tracking is fixed to individual IMEIs. Product/opening inventory and stock adjustments accept one row per physical device with independent RAM, storage, color, cost, price and MRP; matching configurations reuse a variant under the same model. Received memory/color cannot silently change. Sales search names and identities together, or scan either IMEI/serial, and select the actual branch device/variant. Purchasing adds another model configuration inline and keeps its separate cost/payable line. Standard and custom brands, category/brand catalog filters, expanded memory choices and GST split explanations are available.

The additive `DeviceVariantsAndBrands` migration introduces the brand list and missing selectable tax rates. It backfills brand suggestions from existing products, preserves existing catalog/stock/financial records and grants the new master table only SELECT/INSERT to an existing `invora_runtime` role.

## Purchase capture and lookup update

Purchases now show editable IMEI/serial fields under every serialized item. Quantity increases create additional blank device rows without discarding captured identities; reducing a populated quantity requires clearing the extra rows first. Phone categories require one IMEI per physical phone, including custom phone categories. Purchase review validates every identity and existing-stock duplicates before posting.

Product lookup displays clickable purchase results and matches combined brand/model/memory terms, including 3/32. Contact lookup displays matching names/mobile numbers, searches beyond the initial page, supports inline creation and preserves the cart when changing the customer/supplier. Shared contact lookup also serves payments, LenDen and used-device acquisition. Directory search responds while typing. Purchase screens provide prominent inline brand/category creation. The new purchase-item dialog captures product details, purchase quantity/cost and every phone identity in one flow, preserving those details in the bill. “Add another phone” works both inside that dialog and under purchase lines. Stock and supplier money remain posted only by purchase completion. Quantity electronics and serial-only electronics use their appropriate tracking rules. Bill controls are disabled while reviewing/posting. Global search responds while typing and links to customer/supplier accounts and filtered catalog/inventory results. Device fields use full-width controls and the counter cart adapts to small screens. Interface fonts are bundled locally with their free licenses, removing external font requests from builds and page loading.

The additive `CatalogCategories` migration seeds Mobile, Electronics and Accessory, preserves existing category names, and stores the IMEI requirement for custom categories. It grants SELECT/INSERT on the new master table to an existing runtime role. Existing owner credentials, inventory and financial history are retained.

## Correctness boundaries

All retail mutations require a request key. A PostgreSQL transaction lock serializes postings for this single-business deployment. Stock, ledger, allocations, sequence, audit and replay receipt commit together; a failed exchange or stock validation rolls everything back. Stored replay permissions/branches are checked again before returning a cached result. The browser retains pending request keys across tab refresh without storing bearer tokens or request payloads.

Posted invoices/items, payments, ledger entries, corrections and movements are append-only, protected both by EF checks and PostgreSQL triggers. Production uses a separate runtime role with no schema ownership/migration privileges. Invoice corrections preserve the original snapshot and create linked credit notes. Refunds represent separate real money movements. Released/refunded advances cannot be allocated again. Used-device exchange is noncash settlement rather than a fabricated cash payment.

Quantity receipts distribute rounding paise across cost layers/units so valuation equals the posted receipt cost. Returns track which FIFO allocations have already been returned; damaged/repair assessments do not silently restore sellable quantity. Partial transfer receipt makes only the received goods available; cancellation restores only the unreceived goods physically returned to the source.

Supplier invoice uniqueness is enforced by supplier/document reference/financial year. Financial calendar changes are blocked after the first purchase/sale draft. Historical customer/seller details on sales and payment receipts do not change when current master data is edited.

Documents are private downloads, linked to concrete purchases/expenses, checked for size and PDF/JPEG/PNG signatures, and authorized through their parent branch. Purchasing documents also require cost access. They are not exposed as public executable/static files.

## Verification and artifacts

Builds have zero warnings/errors. The final test counts and acceptance evidence are recorded in [testing](testing.md). Production UI compilation and both container builds pass. Container-based browser acceptance also passes with the restricted database role. Encrypted restore signature verification passed; the restored database returned no discrepancies in the reconciliation checks.

The local development stack is available at `http://127.0.0.1:8080`. Private generated credentials are in the ignored `.env`; use `INVORA_BOOTSTRAP_KEY` for first-time setup only. Usability acceptance runs in a separate synthetic database; the user workspace is preserved.

Generated local artifacts under ignored `artifacts/`: desktop/mobile screenshots and `invoice-multipage-fixture.pdf`. Tests use synthetic fixtures; these are not live store transactions.

## Remaining environment acceptance and extensions

- A real USB/Bluetooth/2.4 GHz HID scanner and store printer must be checked at the counter; synthetic keyboard events cannot verify hardware.
- Live AWS rollout needs an account/profile, region, chosen instance, domain, real certificates and private off-host backup destination. Local TLS configuration and deployment scripts do not claim that these external resources exist.
- Backup schedules, off-host transfer/retention and alerts must be configured on the chosen host. The restore rehearsal verifies the supplied mechanism, not an operational off-site schedule.
- PDF download supports browser/system printing. Official government e-invoice registration/IRN/QR and statutory filing require a real integration; no mock official data is generated.
- Percentage GST/cess and line discounts are implemented. Specific-quantity cess, freight/header charge allocation, arbitrary journal adjustments and provider integrations are extensions requiring their own accounting rules.
- Named access templates populate direct grants; a custom reusable role editor, MFA and provider-backed account recovery are extensions.
- Used-device assessments and source/condition/history are implemented. Repair work orders, repair resolution/cost capitalization, device photo galleries, finance-provider separation, automated messaging integrations, subscription billing, cloud object storage and AI/OCR remain the Phase 3/4 extensions described in the design.

Free PDF license: [PDFsharp/MigraDoc MIT](https://docs.pdfsharp.net/General/License/License.html). Embedded Noto Sans font license is included with the fonts. Nginx stable image version was checked against [official image tags](https://hub.docker.com/_/nginx/tags); CI action versions were checked against their official release APIs.
