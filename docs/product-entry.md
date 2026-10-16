# Adding and editing products

Use the normal **Add product** action in Inventory or Catalog, or **Add product here** in New sale and Purchases. The same form adapts to category and tracking type. Quantity items use a short layout; phones and serial devices capture individual identities.

## An item is in the shop but was never entered

1. In New sale, choose **Add product here** and select an accessory category. Enter the item name. If it already exists, select the matching item instead of making a second catalog entry.
2. For a new item, enter its category and selling price. Enter the **quantity to record** and the **cost per item** for the physical goods that were missing from the records.
3. Choose **Save product & stock**. Stock is recorded immediately and one item goes into the sale. Set the quantity being sold in the cart, then review and complete the bill normally.

For example, count five unrecorded cases, save quantity **5**, then sell **2** from the cart. After completion, three remain in stock. The cost entered is used for profit; do not leave it as zero unless those goods were obtained free.

Saving stock and completing the sale are separate actions. Closing the accessory form before saving creates nothing. Leaving an unfinished sale after saving keeps the counted inventory. The sale changes stock, payments and customer dues only when completed.

**Quantity to record means additional unrecorded goods**, not the new total stock balance. If the app already shows three and you found two more unrecorded items, enter two. Use Purchases for a supplier invoice and its amount payable; this counter entry creates no supplier invoice, payment or supplier debt.

## Configure billing once

An owner can open **Category billing defaults** in Catalog or **Configure category defaults** in Business settings. Save an appropriate HSN and an existing tax rate for a specific category, such as phone cases or USB cables. A generic accessories category may contain items with different billing classifications, so give them separate categories or edit billing details for the item.

New items reuse that category's billing details. Without a preset, expand billing details and enter them; an owner can select **Remember this billing setup for this category** while saving counted quantity stock. Details remain editable before the new product is created. Preset changes and clearing a preset do not rewrite existing products, invoices or historical tax.

The product/purchase form loads saved category billing defaults. Actual sales apply the shop's GST registration/composition settings. The app does not guess an item's HSN or tax rate from its name or price.

## Less typing, clear tracking

- New accessory categories default to **quantity tracking**; choose individual device tracking for electronics that need serial numbers. Quantity entry does not ask for RAM, ROM, IMEI or serial numbers; optional colour is under more details.
- Brand defaults to **Generic**, MRP to the selling price, and manufacturer warranty to **0 months**. Brand, barcode, MRP and warranty remain optional overrides.
- A unique SKU is generated automatically, with an optional custom SKU before first save. The existing product QR labels work for these items, and an optional manufacturer barcode can be scanned in New sale.
- Search results show available quantities and can be clicked directly. Repeated accessory scans/additions increase the existing cart line. A shortage offers **Record missing stock** to authorized staff; recording stock for an existing cart line keeps its sale quantity as entered.
- Phones and individually tracked electronics continue through the IMEI/serial workflow. The quantity-stock endpoint rejects individually tracked products/categories.

## Access and record safety

Creating a quantity product with stock requires `inventory.view`, `inventory.create`, `inventory.adjust` and `inventory.cost.view` in an authorized branch. Recording more stock for an existing quantity item does not require catalog-creation permission. Saving category billing defaults requires `business.settings`. Normal sales permissions still apply.

Product creation, counted stock, FIFO cost layers, movement history and any requested billing-default save are one transaction with an idempotency key. A retry cannot add the same stock twice. Duplicate exact item names within the same brand/category and duplicate barcodes are rejected with guidance to reuse the existing item. Completing a sale still checks stock server-side, so a concurrent counter cannot oversell it.

Defaults are stored in the existing business-settings JSON in PostgreSQL; counted goods use the existing catalog, balance, cost-layer, movement and audit tables. No database migration or reset is needed. Ordinary business-settings saves preserve accessory defaults.

## Editing an existing product

Choose **Edit** in Catalog or **Edit product** on an Inventory row. You can update name, brand, category, HSN, tax, barcode, selling price, MRP and manufacturer warranty. Quantity products also allow an optional colour. Name, brand, category, HSN, tax and warranty belong to the model and apply to its variants. Changes are audited with before/after details.

SKU and quantity-versus-device tracking remain fixed after creation so existing stock references stay consistent. Quantity and cost changes use stock adjustments/receiving, not catalog editing. For a received phone, use **Edit device** to correct its colour, RAM/ROM, inspection details or inspection notes. IMEI / serial identities stay protected. Selling price and MRP edits apply to the selected variant on future bills, including its available stock.

Posted invoices keep their original snapshot, price, HSN and tax. Product edits do not change stock quantities, FIFO costs, payments or ledger balances. The product QR identity remains the same; changing a manufacturer barcode replaces that raw barcode lookup.
