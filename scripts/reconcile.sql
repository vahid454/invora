-- Every returned row is a reconciliation error. Run on a quiet snapshot or restored DB.
SELECT 'quantity_layers' AS check_name, b."BranchId", b."ProductVariantId", b."Quantity"::numeric AS recorded,
       COALESCE((SELECT SUM(l."RemainingQuantity") FROM "StockCostLayer" l WHERE l."BranchId"=b."BranchId" AND l."ProductVariantId"=b."ProductVariantId"),0)::numeric AS expected
FROM "StockBalance" b
WHERE b."Quantity" <> COALESCE((SELECT SUM(l."RemainingQuantity") FROM "StockCostLayer" l WHERE l."BranchId"=b."BranchId" AND l."ProductVariantId"=b."ProductVariantId"),0);
SELECT 'sale_totals' AS check_name, s."Id", s."Total", COALESCE((SELECT SUM(i."Total") FROM "SaleItem" i WHERE i."SaleId"=s."Id"),0) AS expected
FROM "Sale" s WHERE s."Status" NOT IN ('Draft','Abandoned') AND s."Total" <> COALESCE((SELECT SUM(i."Total") FROM "SaleItem" i WHERE i."SaleId"=s."Id"),0);
SELECT 'purchase_totals' AS check_name, p."Id", p."Total", COALESCE((SELECT SUM(i."Total") FROM "PurchaseItem" i WHERE i."PurchaseId"=p."Id"),0) AS expected
FROM "Purchase" p WHERE p."Status"='Completed' AND p."Total" <> COALESCE((SELECT SUM(i."Total") FROM "PurchaseItem" i WHERE i."PurchaseId"=p."Id"),0);
SELECT 'payment_overallocation' AS check_name,p."Id",p."Amount",SUM(a."Amount"-COALESCE((SELECT SUM(r."Amount") FROM "AllocationReversal" r WHERE r."PaymentAllocationId"=a."Id"),0)) AS allocated
FROM "Payment" p JOIN "PaymentAllocation" a ON a."PaymentId"=p."Id"
GROUP BY p."Id",p."Amount" HAVING SUM(a."Amount"-COALESCE((SELECT SUM(r."Amount") FROM "AllocationReversal" r WHERE r."PaymentAllocationId"=a."Id"),0))>p."Amount";
SELECT 'duplicate_identity' AS check_name,"Kind","Value",COUNT(*) FROM "UnitIdentifier" GROUP BY "Kind","Value" HAVING COUNT(*)>1;
SELECT 'sold_unit_link' AS check_name,u."Id",u."ActiveSaleItemId" FROM "StockUnit" u
WHERE (u."Status"=2 AND (u."ActiveSaleItemId" IS NULL OR NOT EXISTS(SELECT 1 FROM "SaleItem" i WHERE i."Id"=u."ActiveSaleItemId" AND i."StockUnitId"=u."Id"))) OR (u."Status"<>2 AND u."ActiveSaleItemId" IS NOT NULL);
SELECT 'payment_ledger' AS check_name,p."Id",p."Amount" FROM "Payment" p
WHERE NOT EXISTS(SELECT 1 FROM "LedgerEntry" e WHERE e."PaymentId"=p."Id" AND (CASE WHEN p."Direction"='In' THEN e."Credit" ELSE e."Debit" END)=p."Amount")
OR (SELECT COUNT(*) FROM "LedgerEntry" e WHERE e."PaymentId"=p."Id")<>1;
SELECT 'sale_ledger' AS check_name,s."Id",s."Total" FROM "Sale" s WHERE s."Status" NOT IN ('Draft','Abandoned')
AND NOT EXISTS(SELECT 1 FROM "LedgerEntry" e WHERE e."SaleId"=s."Id" AND e."Kind"='Sale' AND e."Debit"=s."Total");
SELECT 'return_quantity' AS check_name,i."Id",i."Quantity" FROM "SaleItem" i
WHERE COALESCE((SELECT SUM(r."Quantity") FROM "SaleReturnItem" r WHERE r."SaleItemId"=i."Id"),0)>i."Quantity";
SELECT 'quantity_movements' AS check_name,b."BranchId",b."ProductVariantId",b."Quantity",COALESCE(SUM(m."Quantity"),0) AS expected
FROM "StockBalance" b LEFT JOIN "InventoryMovement" m ON m."BranchId"=b."BranchId" AND m."ProductVariantId"=b."ProductVariantId"
GROUP BY b."BranchId",b."ProductVariantId",b."Quantity" HAVING b."Quantity"<>COALESCE(SUM(m."Quantity"),0);
