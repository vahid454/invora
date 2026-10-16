# LenDen: old register dues, promises and collections

Open **LenDen → Collections & promises**. This screen combines unpaid Invora invoices with amounts brought forward from your old register. Customer balances and independent Give / receive money remain available in their existing tabs.

## Bring your register into Invora

For a few entries, choose **Add existing due**. Find the customer by either phone number, or create the customer in the same form. Enter:

- A unique register / old invoice reference for this branch, such as `REGISTER-2026-001`.
- The amount **still owed to your shop**, after all earlier receipts.
- Original sale/register date, promised payment date, and the phone model or reason.
- Optionally, the finance company or application reference.

Example: the customer still owes ₹5,000 of a phone down payment. Record ₹5,000, even if the phone originally cost ₹40,000. Receiving ₹2,000 later leaves ₹3,000 to collect.

An opening due adds to the customer trading ledger and receivables on the day it is recorded. The original sale/register date is retained as context and for oldest-first allocation; Invora does not reconstruct past account balances from a remaining amount imported today. It does **not** create a sale, GST, profit, cash withdrawal, stock receipt or stock deduction. Do not use “Give money” for an old unpaid sale: Give money records an actual outgoing payment.

An invoice already posted in Invora appears automatically. Do not import its balance again under a different reference. Exact matching invoice/reference numbers are rejected, and the import review asks you to confirm that the records are not already present. The application cannot infer that two differently named register entries describe the same historical sale.

The finance reference is a note, not a lender account. Record only amounts owed to the shop. Finance-company EMIs, lender settlement receivables and their allocation are not implemented by this feature. For an Invora invoice, record money actually received through Payments; do not reduce its balance merely because finance was approved.

## Import many entries from Excel

1. Choose **Import register → Download CSV template**, or use [the blank template](templates/existing-dues.csv).
2. Fill one row per unpaid entry and save as CSV. Use plain amounts (`5000.50`) and dates in `yyyy-MM-dd` format. Preserve phone columns as text. Keep all nine headers, even when optional cells are empty.
3. Upload the file or paste its contents. Choose **Validate & preview**. The preview shows every entry, customer match and total; errors identify the CSV row.
4. Check the customers, references and remaining amounts. Confirm that the entries are not already recorded, then import.

| Column | Meaning |
|---|---|
| `name` | Customer's name; must match an existing contact's name when a phone matches |
| `phone` | Primary contact; Indian `+91` and the local ten-digit number are equivalent |
| `alternatePhone` | Optional second number; use a different number |
| `reference` | Unique reference per branch, case-insensitive; required even for paper register entries |
| `description` | Phone/model or reason, such as remaining down payment |
| `financeReference` | Optional company/application reference; information only |
| `amount` | Currently unpaid amount owed to the shop, maximum two decimal places |
| `originalDate` | Original sale/register date, between 2000-01-01 and today |
| `dueDate` | Promised/due date, not earlier than the original date |

Use up to **500 rows and 1 MB per file**. Several entries can belong to the same customer. The importer checks both phone numbers, refuses ambiguous matches, requires matching names, and refuses to silently discard a new alternate number on an existing contact. Correct the customer first and validate again when necessary.

The preview is tied to the employee who reviewed it and expires after one day. Commit rechecks contacts and references and imports the entire file in one transaction. If any row becomes invalid, nothing from that commit is saved. Retrying the same request does not duplicate it; repeating a committed reference is rejected. Cancelled references stay reserved for audit history.

## Receive money and keep promises

Use **Receive** beside the entry. It opens the usual payment form with that due selected. Choose cash, UPI, card or another method and enter the amount actually received.

- A smaller receipt leaves the remainder due.
- A larger receipt settles the entry and retains excess money on the trading account.
- Without choosing a document, automatic allocation settles the oldest invoice/register dues by original date, with a maximum of 200 allocations per receipt; excess remains on account.
- Advance / money on account deliberately leaves the receipt unallocated. **Apply existing receipts** in Collections applies eligible existing receipts to oldest dues without creating another payment. Up to 200 allocations are made per action; repeat if necessary.
- Reversing a receipt restores the amount owed. Refunds continue to use available account credit and the existing payment controls.

**Promise / notes** records a date for this entry, with shortcuts for 10, 20 or 30 days from today. Add a call/reminder note or next contact date. Earlier promises remain in history; clearing a promise restores the original due date for collection filtering. Account-level promises in Customer balances remain separate. A promise never records money received.

Use **Correct entry** to cancel a mistaken opening due with a reasoned ledger reversal, then create the corrected entry with a new reference. Entries with allocated receipts must have those receipts reversed first. This is a correction workflow, not a debt write-off or a refund. Original entries are retained.

## Reports and reminder queue

Filter by customer, either phone, register/invoice reference, device description, finance reference, unpaid/overdue/promised/settled status and an inclusive due/promise date range. “Due / follow-up today” also includes overdue contact dates. Export uses the same filters and all matching rows, not just the visible page.

The CSV includes both contact numbers, source, reference, original date, due/promise date, applied receipts/exchange, outstanding amount and overdue days. The invoice amount is net of credit notes. It also identifies unallocated credit and the customer trading balance. **Do not sum repeated customer balances across document rows.** The on-screen total is unpaid document balances; unallocated customer money is shown separately. The current register is bounded to 20,000 branch documents and fails explicitly beyond that limit rather than silently truncating totals.

For reminders:

1. Select eligible customers on a page; selections can be accumulated across pages. Changing filters clears the selection.
2. Choose **Review WhatsApp reminders**. Each customer appears once, even when several selected rows belong to them.
3. Invora refreshes the account and prepares one message covering **all current unpaid invoice/register dues in that branch**, including dues outside the visible filter. Review the contact, amount and text.
4. Open the WhatsApp draft and send it yourself, then continue to the next customer. SMS and call links are also available. Use Refresh balance if the queue has been open while payments were received.

Settled accounts cannot get a collection draft. Accounts with unallocated credit are blocked until reconciled, so the draft cannot ask for money already received. Independent LenDen is excluded from this collection message and remains available through Customer balances. Opening a draft is **not** logged as sent/delivered. Record the actual conversation or reminder outcome in Promise / notes if needed.

This is a guided batch of reviewed drafts, not unattended WhatsApp broadcasting. No customer messages are automatically sent. Automated delivery would require a separately configured business messaging integration and appropriate consent/templates. Existing customer-statement PDFs remain available from Customer balances / the customer page for sharing individually.

## Storage and access

Data stays in the existing local PostgreSQL database. The additive `ExistingCustomerDues` migration adds an immutable opening-due table and nullable links on allocations, ledger entries and follow-ups; it does not reset or rewrite existing financial records. Existing backups cover these records. Updates use the normal explicit migration step in the Windows/Mac launchers.

View requires `customers.credit.view` and branch membership. Opening entries/corrections require `ledger.adjust`; imports additionally require `customers.manage`. Follow-ups require `customers.manage`; receipts and allocating recorded money require `payments.create`; CSV export also requires `reports.export`. The server checks permissions, branch/customer ownership, amounts and allocations on every relevant operation. See [storage and security](data-storage-security.md).
