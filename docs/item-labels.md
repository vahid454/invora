# Print and use item QR stickers

1. Receive the purchase or add opening stock. Enter each phone's own IMEI, RAM, storage and colour.
2. Open **Inventory → Devices**. Use **Print label** on an item, or filter the list and choose **Print labels** for the current page. Select only the items you want.
3. For accessories sold by quantity, use **Quantity stock** or the **Product catalog**. Each SKU shares one QR, so set copies to the number of identical items to sticker. A serial-tracked electronic item uses an individual device label.
4. Choose the paper your printer actually uses: **50 × 30 mm** by default, **60 × 40 mm**, or **70 × 50 mm** for longer details. Download the PDF and open it in your PDF viewer. Each PDF page is one sticker.
5. In the printer dialog select the matching label paper, **Actual size / 100%**, one page per sheet and no fit/shrink scaling. Check orientation and the printer's gap calibration. Print one first, scan it, then print the batch. The app downloads a PDF; it does not install printer drivers or send raw thermal-printer commands.

Labels show model, memory/colour when present, condition for devices, a readable IMEI/serial or SKU, and a QR. Long model names can be abbreviated on the compact PDF; identities always remain complete. If details do not fit, choose a larger sticker. No price is printed: changing the selling price does not mean replacing stickers. Use a **2D QR-capable scanner** in keyboard mode with Enter (or type/paste the code). A 1D-only barcode reader cannot read a QR.

In **New sale**, focus the scan box and scan the sticker. The exact available phone is added with its current variant/price; a quantity product scan adds one unit. Scanning the same phone twice cannot add a duplicate, while repeating a quantity code increases quantity. Sold or unavailable stock remains guarded by the normal checkout rules. The same code works in stock transfers, inventory, movement history and catalog search. Inventory automatically selects Devices or Quantity stock for a typed item code. Original IMEI, serial and manufacturer barcodes still work.

A device label identifies that physical stock record permanently. Correcting its colour/memory or transferring it does not change its QR. Reprint after a visible-detail correction if necessary; the old QR still resolves current details. A used phone with no manufacturer identity can use its existing shop tracking tag. Quantity labels identify a SKU, not a unique accessory unit. Do not attach a phone's sticker to another phone. No stock or accounting records are created by printing.

Labels are made locally with the free, MIT-licensed [QRCoder](https://github.com/Shane32/QRCoder) and the existing PDFsharp stack. QR content is an internal identifier, not a website link or customer record. Phone camera links are not a supported stock lookup; use the app's authenticated scan/search field. Restoring the original database preserves label IDs; a new independent database cannot resolve the old shop's labels.

Limits: 50 distinct items per request, 1–100 copies per item and 200 stickers per PDF. Larger jobs can be split across pages/batches. Printing requires inventory-view permission and access to the selected branch; cost permission is not needed. Physical printer/scanner acceptance still needs the shop's actual hardware.
