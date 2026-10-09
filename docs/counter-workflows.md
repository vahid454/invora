# Everyday counter workflows

## A sale without leaving the counter

Open **Sales → New sale**. Find stock by model name, brand, RAM, color, IMEI or serial. Available phones appear directly with their identity and selling price; choose the exact device. The product picker also opens a chooser showing every available memory/color variant of that model in the active branch. Scanning an IMEI, either SIM slot, or serial adds that exact device directly. Quantity products add normally.

Use **Find customer** to search by either mobile number or name and click the matching contact. Multiple name words, formatted phone numbers and a country-code prefix also work; lookup searches the full directory rather than just the first page. **Change customer** clears the current selection while preserving the cart. If the customer is new, choose **New customer**, enter their name/mobile and save. The customer is selected automatically and your sale items stay in place. Address, state, email and GSTIN are optional details inside the modal. Supply state follows the selected customer and shop state.

Enter cash/UPI/card, review the server totals and complete the bill. Credit sales still require credit access and a due date. Existing payment and stock checks apply to owners and employees alike.

The same contact lookup/create component works for suppliers during purchasing and for payment/LenDen accounts.

## Add mobiles and electronics

**Catalog → Add product** and **Create new product here** on a sale/purchase share one product form. Enter display/item name, brand, category, selling price, MRP, HSN and tax rate. A new serialized model captures RAM, ROM/storage and colour in each phone's IMEI/serial row; quantity products and existing variant editing retain specification fields. Mobile phones always use individual IMEI tracking; there is no quantity-tracking choice for mobiles. Other electronics may use serial tracking, while accessories can use quantity tracking. SKU is generated if left blank; barcode and warranty are optional.

Memory suggestions include 2/32 and 3/32 configurations, RAM from 2 to 24 GB and storage from 16 GB to 2 TB. Custom values remain possible. Built-in brands include Apple, Samsung, Realme, Oppo, Redmi, Poco, Honor, Moto, Tecno, Infinix and others. Type a brand or choose **Add new brand** to save/select it without leaving the form. Catalog category and brand filters help organize stock. **Add new category** saves a custom category; choose whether it contains mobile phones (IMEI required) or other electronics/accessories. Mobile and Electronics remain available by default.

Tax choices include 0%, 5%, 18% and 40% as selectable rates, plus owner-defined rates and cess. Existing rates remain intact; requesting an equivalent active GST/cess rate reuses it. The form explains total GST versus half-rate CGST/SGST or full-rate IGST. New mobile forms suggest an existing 18% rate; the operator can select the applicable configured rate. CBIC lists heading 8517 at 9% CGST + 9% SGST / 18% IGST in its [goods rate table](https://cbic-gst.gov.in/gst-goods-services-rates.html). Other categories require the appropriate item HSN and tax selection. Sales use the business GST-registration/composition settings and issued invoices retain their original tax snapshots.

For already-owned stock, enable **Add stock already in this shop now**. Choose default memory/color/prices/cost and add one row per physical device. Each row can override RAM, storage, color, unit cost, selling price and MRP, with IMEI 1, optional IMEI 2 and/or serial. Mobile rows require an IMEI; other serialized electronics can use a serial alone. Quantity comes from the device rows. For example, enter a blue 8/128 S24 FE and a black 12/256 S24 FE together: they share a model but keep separate variants, identities, costs and prices. Product, variants and devices commit atomically; one duplicate identity rejects the complete batch.

**Inventory → Opening / adjust stock** provides the same per-device rows for existing models. It creates/reuses the appropriate variants. To remove stock, choose **Write off / reduce stock** and select the exact available devices; quantity follows the selection. Quantity accessories use an explicit increase/decrease count. Receiving a device fixes its memory and color; use a new variant for a different configuration. Shared model edits (name, brand, category, HSN, tax, warranty) apply across its variants, while historical invoice snapshots stay fixed.

Opening inventory creates stock value without inventing a supplier payable or cash payment. For a new supplier invoice, receive through **Purchases**. Search by brand, model, memory shorthand such as **3/32**, or barcode and click **Add to purchase**. Create a missing brand, category or product directly in this screen. The **New purchase item** dialog includes purchase quantity, default purchase cost and one row per phone/device. Each row can have its own RAM, storage, color, cost, selling price and MRP; different configurations become separate purchase items under the same model. **Add another phone** creates the next row, keeping the IMEIs already entered. Save the product and its prepared identities into the purchase together; stock and the supplier balance are recorded when the purchase is completed. Quantity-tracked electronics/accessories use a count and cost without device identities. Other serialized electronics may use a serial number alone. Select a model and choose **Another memory / color for this model** to create/select a variant inline, add each configuration as its own purchase line, enter its cost and enter or scan its devices. Every individually tracked item immediately shows one editable identity row per unit, with IMEI 1, optional IMEI 2 and serial fields. Increasing quantity or choosing **Add another phone** adds blank rows while preserving existing identities. Use **Remove device** to delete an accidental extra phone immediately; quantity follows the rows. Reducing the quantity field alone preserves populated identities until you explicitly clear/remove them. Clicking an identity field selects that item as the scanner target; scanning fills the next empty device row. Review remains disabled until every phone has a valid 15-digit IMEI, and duplicate identities are rejected before posting. That purchase posts the real supplier payable, exact device costs and stock together. Returns/refunds continue to use the original device/variant and recorded cost.

## Search across the shop

The top search responds while typing. On smaller screens, use the search button in the top bar. Look up contacts by name/mobile, products by brand/model/memory, and devices by IMEI/serial. Results link directly to customer/supplier accounts, a filtered catalog or the matching inventory. Product filters can be cleared from the counter when they no longer match the item being entered.

## Independent LenDen

**LenDen → Customer balances** shows sales/advance balance and independent LenDen separately, plus their combined balance. Search by name/mobile, open a statement, or select **Give / receive money**. You can add a new contact in this flow without a sale.

**Money given** increases what the person owes the shop. **Money received** reduces that balance; a negative balance means the shop holds money owed to the person. Record the actual method/date/reference and a required reason. These entries create real payment receipts, cash/bank flow and immutable ledger entries, without creating sales revenue or GST.

Independent entries cannot be allocated to invoices or used as invoice advances/refunds. Invoice payments use **Receive invoice payment** or the **Invoice dues** tab. Corrections use a reasoned reversal in **Payment history**, preserving the original. Statements and receipts show both movements and their reversals.

## Co-owner and trusted employees

**Staff & access** offers **Co-owner / store manager**, **Trusted counter staff**, Cashier, Inventory manager and Accountant templates. Templates populate direct permissions; review the grants and branch membership before saving. A manager is still a staff account: the original owner's account protections remain in force.

Independent LenDen requires `ledger.adjust`, `payments.create` and `customers.credit.view`. Inline customer creation requires `customers.manage`; inline supplier creation requires `supplier.manage`. Product creation requires `inventory.create`; opening stock also requires `inventory.adjust` and `inventory.cost.view`. Configuring tax rates requires `business.settings`. Existing staff grants are not automatically expanded.

## A customer's old phone in an exchange

Enable **Accept used device in exchange** on a sale. Enter the **Old device name**, brand, memory, color, condition and agreed exchange value. These describe the phone handed over by the customer. You can choose an existing catalog variant under the optional details instead. Old IMEIs, serial, seller name, Aadhaar last four, battery health and warranty are optional. If there is no physical identity, the received phone gets a unique shop tracking tag that can be searched/scanned and used for an exact write-off. Exchange credit reduces the new invoice's due and records the old phone at the agreed acquisition cost; it does not create a cash receipt.

## Reminders, promises and customer statements

Add an optional second mobile when creating or editing a customer. Both numbers work in lookup. In **LenDen**, filter by sales/independent dues, credit, payment promise or follow-up date. **Remind / promise** opens the contact panel. Choose which number to use, edit the message, then open a **WhatsApp draft**, **SMS draft** or **Call customer**. You decide whether to send. Record the conversation or reminder outcome afterwards.

For a payment promise, save its date, optional promised amount, next contact date and note. **Close / clear previous promise** records a correction without removing earlier conversations. A promise does not record money; receive the real payment through the payment/LenDen flow when it arrives. Each account's debt/credit remains separate in the filters.

Authorized staff can download a customer account PDF for a selected period with opening/closing balances and running entries. **Share statement** uses file sharing where the browser supports it; otherwise it downloads the PDF for attachment to the message. Statement export requires `customers.credit.view` and `reports.export`; saving follow-ups also requires `customers.manage`.

## Invoice payment, advance or giving money back

On **Record payment**, select the account and **Payment purpose**. **Receive / pay money** does not require an invoice selection: leave it automatic to settle the oldest branch dues, or choose one invoice. Enter any positive supported two-decimal amount; a partial payment leaves dues and excess stays on the trading account. The preview shows the split, and the server caps actual allocations to current dues. Choose **Advance / money on account** to keep the whole payment unallocated. Choose **Give money back to customer** to return available trading credit, or **Receive supplier refund** for money returned by a supplier; the form shows the credit limit and removes invoice allocation. Independent money given/returned uses the separate LenDen flow.

## A branch stock handoff

Open **Stock transfers → Dispatch stock**. The source is the active branch; choose a different authorized receiving branch. A staff member with `branches.manage` can add that branch here. Search or scan each phone's identity, or scan an accessory barcode and set its quantity. Remove any accidental entries before dispatching. Phones keep their individual identity and cost; quantity items retain their cost layers.

Dispatch places goods in transit. Open the transfer, use **Switch to [destination] to receive**, count each item actually received, and confirm receipt. Remaining quantities stay in transit for later receipt. A source-side cancellation records goods physically returned to the source; received goods require a new return transfer. No received stock is silently moved back.

## Reports for the shop

Use **Today**, **Last 7 days**, **This month**, **Last month** or **Financial year**, or choose a custom period up to one year. Daily bars include sales and returns on their posting dates; top-product net quantities include returns of earlier sales. Stock counts, aging and transit figures show the current stock. Authorized staff can export summary, daily and product CSVs; customer statements are exported from the customer/LenDen panel.

## Invoice and account lookup

Use Sales & invoices to search a customer's name, either phone number (including formatted country codes), the tax invoice number or the shop reference. New invoices show a dated reference, with a separate short legal invoice number and printed issue time. Choose the actual place of supply in a sale; the server derives the GST split. See the [owner guide](owner-guide.md) for examples and registration setup.

Inventory filters narrow phones by brand and status, including available, sold and in-transit devices. Create a phone model once, then enter colour and memory on each IMEI row. Supplier balances show **Dena hai** for money you need to pay and **Lena hai** for credit/money to recover. Customer statements can show the sales account, independent LenDen or both; their net does not settle either account. Reminders use the latest promise date and open a draft for the operator to send.
