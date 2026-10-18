# Invora product and correction specification

Agreed scope: 7 October 2026. Invora serves a mobile/electronics shop owner, co-owner and authorized staff. One business owns each PostgreSQL database; branches share that business. The application uses .NET 10, Angular and PostgreSQL, with free PDF generation. This document describes the intended behavior; verification results live in `docs/testing.md`.

## Core operating rules

- Receive new phones through purchases or counted opening stock, capturing one unique IMEI/serial set per physical device. Each device can have its own colour, RAM and storage. Quantity goods use cost layers.
- Search customers by name or either phone, and products by name, model, memory, IMEI or serial. Create missing contacts/catalog entries inside the counter flow.
- Post sales, purchases, returns, exchanges, transfers and payments transactionally and idempotently. Corrections preserve original stock movements, invoice snapshots, payments and ledger entries.
- Record the full payment received/paid. Invoice selection is optional; automatic allocation settles oldest branch dues, a selected invoice receives at most its remaining dues, and excess stays on account. Independent LenDen is a separate account.
- Show customer/supplier money direction clearly: money to receive (lena hai), money to pay (dena hai), with separate accounts and linked reversals/refunds.
- Present LenDen as one customer register with gross **Lena hai** and **Dena hai**, source amounts for sales/old dues and independent money, and invoice/old-due promises on the same page. Opposite-direction obligations must never disappear behind a single net figure or settle without an explicit posting.
- Show the address immediately when adding a customer. Persist product category groups as Mobile, Electronics or Accessories; mobile group requires per-device IMEI tracking. Preserve custom category names within their chosen group.
- Keep shop data on the local PostgreSQL and document volumes until a verified backup/restore moves it. A new-device install must not imply synchronization; preserve the original private configuration, licence/shop identity and matching database/documents, and verify recovery before cutover.

## Final warranty policy

New invoices must state: “Warranty, where applicable, is covered by the manufacturer / authorized service partner, not the shop. Customers must visit the authorized service centre for service. Used phones are sold without warranty.”

- New goods show the configured manufacturer/service-partner warranty duration where applicable.
- Used, refurbished and open-box devices received from customers show their condition and **No warranty**; acquisition/exchange forms cannot promise a shop warranty.
- Warranty wording and device condition are captured when a sale is posted. Already-posted terms remain part of the original invoice history; later settings or inventory edits do not rewrite them.
- Credit notes retain the original document's relevant warranty/condition information.

## Payment reminder

The default editable message uses short paragraphs: greeting; shop name and separate positive sales/LenDen dues; promise date if recorded; a polite request to pay as promised and avoid being recorded as a payment defaulter in the shop's records; invitation to call for a revised date; “Thank you,” followed by the configured shop name and contact number.

- “Defaulter” refers only to the shop's payment records. The message makes no claim about a bank, CIBIL or credit-bureau report.
- The shop name comes from the business profile, and the phone from Business settings. If a phone is missing, show a setup hint rather than inventing a number.
- Drafts stay editable. WhatsApp/SMS open a draft for the operator to send; the application sends no automatic customer messages.
- Refreshing promise/shop details must not overwrite text the operator has edited.

## Guarded inventory corrections

Provide a clear **Edit device** action from Inventory and Used devices.

| Editable | Protected |
|---|---|
| Colour, RAM and storage for this physical device | IMEI1/IMEI2, serial/shop identity, device ID and original receipt |
| Battery health (0–100 or unknown), inspection/accessory/repair notes | Cost, quantity, purchase/ledger amounts and posted invoice snapshots |
| Used-device condition within its existing new/used classification | Stock state, branch and model identity |

- Require `inventory.adjust` permission, authorized branch, available `InStock` status, a reason and the last-seen revision. Reject stale changes and sold/in-transit/unavailable devices.
- Changing specifications selects/creates a variant under the same model for this device; it never edits the shared specifications of other devices.
- Keep an append-only before/after correction history with actor, reason and timestamp. Existing inspections remain in history.
- A correction must not post money, change cost or change the number of physical devices. Future scanning/sales/exports use the corrected description. Supplier return eligibility still follows the original received device.
- Ordinary stock changes, returns and transfers continue through their dedicated posting workflows.

## UI and documents

Keep the existing calm green/paper interface and local fonts. Prioritize clear labels, aligned actions, visible save/loading/error states, keyboard-accessible dialogs and layouts usable at 375px without page overflow. Show protected identifiers alongside edits and human-readable change history.

Invoices separate item totals, amount words, GST summary, tax words and terms. Single-page and multipage layouts retain readable headers and exact posted values. Inventory CSV uses the visible view/filters, readable fields and shop-local dates; cost fields require cost permission.

## Counter search, invoice dates and sale receipts

- LenDen customer search remains a compact single-line field, aligned with the account filter on desktop and stacked on small screens.
- Sales & invoices supports all dates, today, yesterday, the last seven days, this month and custom From/To dates. Both boundaries are inclusive and refer to the invoice business date. Date filters combine with customer/phone/invoice search, pagination and the sales CSV export. Invalid ranges produce a clear error.
- New-sale cash, UPI and card entries count as the actual amounts received. Blank methods mean zero; negative, malformed and fractional-paise amounts are rejected. Decimal amounts are summed without floating-point rounding.
- Review shows method amounts, total received, exchange credit and remaining due. Editing a payment retains the server-calculated bill total for the preview but requires review again before posting. Completion posts each payment method once and allocates it to the invoice; only the unpaid balance remains in LenDen. Fully paid invoices do not require a credit due date. Excess sale payments remain blocked with guidance to record a separate advance.

## Delivery, data and commercial use

Windows uses InvoraSetup.exe; Mac uses a native universal Invora.app distributed in a DMG/ZIP. Both launch the Docker-based application with PostgreSQL and private files in persistent Docker volumes. Secrets stay outside customer release sources; backups cover the database and private documents, with private configuration protected separately. Signed shop-ID licences support activation, paid dates, grace and renewal while retaining read/export access after expiry. See `docs/windows-installation.md`, `docs/macos-installation.md`, `docs/data-storage-security.md` and `docs/commercial-release.md`.

Local Windows startup must reject invalid or mismatched port/browser settings before migration. Installer failures remain visible and updates find stopped as well as running installations. **Check Invora** inspects local startup readiness without changing services, configuration or business records; its optional report contains predefined results rather than private values or logs. Native Windows/ACL/shortcut/hardware acceptance is recorded separately from tests using simulated dependencies.

Mac startup follows the same preservation rules: verify release files before writing, reuse existing/stopped installations, refuse new credentials for existing volumes, restrict operation to a local Docker engine and validate the loopback origin/port. Check status is read-only. Start opens an already healthy shop without interrupting it. Updates require a backup, rebuild the release and pause billing before migrations; a failed build must remain marked for retry. The current ad-hoc app signature does not claim Developer ID/notarization. Desktop apps do not synchronize independent databases or schedule off-host backups automatically.

An owner-authorized operational reset may clear shop transactions and stock only after a recovery backup and explicit retention scope. It must retain selected accounts/settings/catalog, verify those records unchanged, preserve issued-number counters and audit history, revoke old sessions and verify stock/ledger reconciliation. This maintenance operation is separate from normal installers and application correction workflows.

## Acceptance

Verify guarded edit permissions, stale requests, replay, history immutability, one-device-only colour changes, battery validation, purchase return and sale after correction. Verify new/used warranty snapshots and PDFs; paragraph reminder formatting with shop contact and preserved manual edits; desktop/mobile editing and inventory search/export. Run the full relevant backend/browser suites, model/migration checks, production builds and stock/money reconciliation. Test on isolated data, then back up and update the existing installation without resetting its records.

## Permanent item stickers and desktop windows

- Each serialized stock unit has a permanent `INVORA:U:<32 hex GUID>` code; quantity products use `INVORA:P:<32 hex GUID>`. Codes are deterministic identifiers, not credentials. No table or migration is required. Correcting device colour/memory and moving branches preserves the unit's code. Reprints never post stock/money movements.
- Label reads/PDF downloads require inventory-view permission and branch authorization. Device labels belong to the unit's current branch; serialized catalog products direct the operator to the actual devices. Quantity labels identify SKU, not an individual physical piece.
- Thermal PDF sizes are 50×30, 60×40 and 70×50 mm; one sticker per page, 1–100 copies per item, at most 50 distinct items and 200 pages per request. QR modules remain vector shapes with a four-module white border. Full identities cannot be clipped to fit: ask for a larger size. Long model names can be abbreviated on paper; preview retains full text.
- Label previews expose model/specification/category/condition, readable identity/SKU and QR only. Price, cost, customer details and secrets are excluded. Current price/status are checked through the existing sale/transfer workflows; printed prices cannot go stale.
- Scan typed codes in sales and transfers; paste/scan in inventory, catalog or movement search. Raw manufacturer IMEI/serial/barcodes remain supported. Ambiguous raw codes produce a useful selection error, never an arbitrary product or unhandled exception. Repeated quantity scans increase quantity; repeated device scans do not duplicate the device.
- Desktop hosts reuse the local authenticated UI/API: WKWebView on Mac, WebView2 on Windows. Only the configured loopback origin is embedded; external approved URLs use the system handler. No native scripting bridge is exposed. Downloads use a save dialog; browser fallback remains available. Closing the window does not erase data or stop services.

## Owner-facing stock purchase values

- Show tax-inclusive purchase value as the main stock value in Overview, Inventory, stock aging, reports and CSV; label the stored book basis separately as “Cost for profit”. A ₹84,999 tax-inclusive purchase must show ₹84,999 while retaining its original book cost.
- Read immutable posted purchase totals after discount. Device shares reconcile exactly in paise and keep their original receipt order. Quantity values prorate the original bill-line total and round once per product/branch. Do not apply today's tax rate to old receipts.
- Transfers retain source purchase history through FIFO-layer ancestry. Sales, returns, write-offs and partial transfer cancellation follow current availability. Reacquired used phones use the latest acquisition amount, even when an old supplier receipt link remains.
- Opening stock and used-device intake use the entered acquisition amount; do not invent a tax split. Quantity stock values mean all units on hand, with selling price explicitly per unit.
- Preserve all existing cost/profit/tax/ledger calculations and supplier/payment balances. No schema migration or reposting is required. Both valuation fields require inventory.cost.view, including aging and exports.
- Stock is a current snapshot independent of report dates. Keep the old API stockValue field as cost for profit; add stockPurchaseValue for purchase value including tax. See docs/stock-values.md for examples and rounding.

## Product entry, editing and counter stock

- New sale, Catalog and Inventory provide a single Add product form with a simple quantity-item layout: name/category/selling price plus counted quantity and explicit cost. Optional brand defaults to Generic, MRP to selling price, warranty to zero and SKU is generated. No IMEI, RAM or ROM fields appear. Phones/serialized products remain in their identity-capture workflow.
- Search for and reuse existing catalog items. Show available quantities, merge repeated quantity additions into the cart, and offer missing-stock recording for an authorized employee. A new quantity item adds one to the sale; recording for a shortage in an existing cart line keeps its sale quantity.
- Record counted inventory immediately and explain that supplier invoices/payables belong in Purchases. Cancelling before Save writes nothing; abandoning the sale after Save retains the counted stock. Bill completion posts the normal stock, payment and customer-ledger entries.
- Create product + stock + cost layers + audit atomically and idempotently. Reject invalid quantity/money, omitted cost, phone categories, serialized existing products and duplicate catalog identities/barcodes. Never manufacture a supplier or infer an unknown cost.
- Owners can configure shared per-category HSN/tax presets and optionally remember billing when recording counted stock. The product/purchase forms reuse them for new products. Preset edits never rewrite existing products or invoices. General settings saves preserve presets; manage them through the dedicated permission-checked endpoint.
- Enforce branch membership, stock/cost access and catalog-create permission for new products. Category preset edits require business.settings. Keep stock checks authoritative on sale completion. Store defaults in existing BusinessSettings JSON; no migration or reset is required. See docs/product-entry.md.

- Product editing is reachable from Catalog and Inventory. Descriptive details, billing, barcode and prices are editable with a full before/after audit. SKU/tracking type stay fixed, and stock/cost changes use adjustment workflows. Received phone specifications use device correction; IMEI/serial identities stay protected; posted invoice snapshots and financial history remain unchanged.

## Existing customer dues and collection register

- Bring forward only an amount currently owed to the shop, with original date, due date, unique branch/register reference, device/reason and optional finance-company reference. It creates an immutable opening receivable and audit record on the recording day (the original date is context/allocation order, not reconstructed financial history); never a new sale, GST entry, stock movement or payment. Existing Invora invoices appear automatically and must not be imported again. Lender EMIs and lender settlement accounting are outside this feature.
- Receive partial or excess money through the existing Payment service. Allocate at most the document balance; excess stays on the customer trading account. Automatic allocation includes both invoices and old dues in date order. Reversals restore the due. Apply previously recorded receipts without recording cash twice. Independent LenDen remains separate.
- Correct a mistaken opening due by a reasoned ledger reversal; reject cancellation while active payment allocations exist. Preserve the source record and all earlier promises/receipts. Per-document promises/follow-ups are append-only and never overwrite account-level promises.
- CSV import is a reviewed, actor-bound, branch-scoped, one-day preview (up to 500 rows). Validate all rows before commit, reject duplicate references and ambiguous/inconsistent phone/name matching, recheck matching and uniqueness at commit, and commit atomically/idempotently. No partial file import. Preview identifies newly created versus existing customers; preserve second contact numbers.
- Collections includes customer phones, original amount, applied payments/exchange, outstanding amount, due/promise/follow-up dates, filters and CSV export. Account credit is shown separately; repeated customer balances must not be summed as document balances. Opening dues do not count as new sales or profit.
- Guided WhatsApp reminders are drafts, one per selected customer with current unpaid trading dues for the branch. Refresh balances before preparing drafts, skip settled accounts and block customers with unallocated credit until reconciliation. Owner reviews and sends in WhatsApp. Opening a link never means sent/delivered. No automatic bulk messaging integration or actual customer messages are authorized here.

## Internal customer safety

- An owner or co-owner can mark a customer `Blocked` (blacklist) or `FraudConcern` with a required internal reason, then clear the flag with another reason. Every transition is an immutable actor-attributed audit event. The current status is shared across all branches of the one shop database.
- Flagging never deletes invoices, dues, ledger entries, stock history or legitimate receipts. Customer lookup, accounts and collection reports continue to show the customer and clear **DO NOT CONTACT** warnings. Collection CSV suppresses both phone numbers for flagged customers.
- Flagged customers cannot enter reminder queues, generate a contact draft, record a call/reminder/promise or schedule contact. The server rechecks immediately before opening an external call/message draft. New sales, outgoing independent LenDen and used-device acquisitions are blocked. Receiving old payments, internal notes and normal corrections remain available.
- Flag text and reasons stay in the staff application and audit trail; they do not appear in customer invoices, receipts or statements. No automated messages or calls are sent.
