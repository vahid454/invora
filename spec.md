# Invora product and correction specification

Agreed scope: 7 October 2026. Invora serves a mobile/electronics shop owner, co-owner and authorized staff. One business owns each PostgreSQL database; branches share that business. The application uses .NET 10, Angular and PostgreSQL, with free PDF generation. This document describes the intended behavior; verification results live in `docs/testing.md`.

## Core operating rules

- Receive new phones through purchases or counted opening stock, capturing one unique IMEI/serial set per physical device. Each device can have its own colour, RAM and storage. Quantity goods use cost layers.
- Search customers by name or either phone, and products by name, model, memory, IMEI or serial. Create missing contacts/catalog entries inside the counter flow.
- Post sales, purchases, returns, exchanges, transfers and payments transactionally and idempotently. Corrections preserve original stock movements, invoice snapshots, payments and ledger entries.
- Record the full payment received/paid. Invoice selection is optional; automatic allocation settles oldest branch dues, a selected invoice receives at most its remaining dues, and excess stays on account. Independent LenDen is a separate account.
- Show customer/supplier money direction clearly: money to receive (lena hai), money to pay (dena hai), with separate accounts and linked reversals/refunds.

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
