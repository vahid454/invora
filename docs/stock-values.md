# Understanding stock values

The main **Available stock value** is the purchase amount of goods currently available in the selected branch, **including the tax and discounts recorded on their original purchase bills**. It is not the selling price or an estimate of resale value.

For one phone purchased for **₹84,999 including 18% GST**, a shop using Invora's GST-registered, non-composition costing policy sees:

| Figure | Amount | Meaning |
|---|---:|---|
| Purchase value · incl. tax | ₹84,999.00 | Original bill amount represented by that phone |
| Cost for profit | ₹72,033.05 | Stored cost used when calculating profit |
| Original purchase tax | ₹12,965.95 | Difference in this example, already recorded on the purchase |

The dashboard, inventory, stock aging, report summary and inventory/report CSVs use these labels consistently. For **quantity stock**, both stock values cover **all units on hand**; selling price remains a per-unit figure. Cost access permission controls both values, including exports.

## How the amounts follow the goods

- A purchased device keeps its share of its original bill line. Discounts and original GST/cess are already in that total. Changing the product, tax configuration, colour or memory later does not recalculate its purchase amount.
- A bill line shared by several devices is allocated exactly in paise: divide the line total, then distribute any remaining paise in original receipt order. Sold or returned devices retain their positions in that allocation.
- Quantity goods use the original purchase-line amount per unit, prorated to the remaining quantity. Transferred FIFO layers trace back through every transfer to their original receipt. Sum these values for each product in a branch before rounding to two decimal places. With fractional-paise unit averages, independently rounded branch figures can differ by one paisa from a combined unrounded total.
- Opening stock and used-device acquisitions show the acquisition amount entered. Those workflows do not capture a separate purchase-tax split, so no additional tax is inferred. If a previously sold phone is bought back, its new buyback amount replaces the old supplier amount for current valuation.
- Sales, supplier returns and write-offs remove goods from available stock. A customer return restores value when its disposition is **Restock**. Damaged, repair and in-transit devices are excluded. A transfer adds value to the destination only when received; cancellation restores the unreceived quantity to the source.
- Stock values are **current**, even when a report's dates show an earlier sales period. They describe goods on hand and do not depend on whether the supplier has been paid. Supplier dues and customer balances are shown separately.

## Existing data and accounting

This update reads existing purchase items, devices and FIFO layers. It needs no database migration or reposting of transactions. Existing stock costs, invoice tax, profit calculations, supplier/customer balances and payment history remain unchanged.

The API keeps `stockValue` as the existing cost-for-profit total and adds `stockPurchaseValue` for the inclusive purchase total. Inventory rows expose `purchaseValue` alongside `cost`; quantity rows use totals for the remaining quantity. Both are null without `inventory.cost.view`. New displays should use the explicit inclusive field instead of relabelling the older field.
