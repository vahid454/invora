using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PostedHistoryGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
CREATE FUNCTION invora_reject_history_change() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Posted history is append-only' USING ERRCODE = '23514'; END $$;
CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "LedgerEntry" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "InventoryMovement" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "OperationReceipt" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "Payment" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "PaymentAllocation" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "AllocationReversal" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "SaleReturn" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "SaleReturnItem" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "SaleNonCashSettlement" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "NonCashSettlementReversal" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "PurchaseReturn" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "PurchaseReturnItem" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "SaleItem" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "PurchaseItem" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "AccessAudits" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "DocumentFile" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "Expense" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "UnitIdentifier" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "PartyNote" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "DeviceInspection" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "DeviceAcquisition" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
CREATE FUNCTION invora_guard_document() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF OLD."Status" <> 'Draft' THEN
  IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Posted document cannot be deleted' USING ERRCODE = '23514'; END IF;
  IF TG_TABLE_NAME = 'Purchase' OR (to_jsonb(NEW) - 'Status') IS DISTINCT FROM (to_jsonb(OLD) - 'Status') THEN RAISE EXCEPTION 'Posted document values are immutable' USING ERRCODE = '23514'; END IF;
 END IF;
 IF TG_OP = 'DELETE' THEN RETURN OLD; END IF; RETURN NEW;
END $$;
CREATE TRIGGER immutable_document BEFORE UPDATE OR DELETE ON "Sale" FOR EACH ROW EXECUTE FUNCTION invora_guard_document();
CREATE TRIGGER immutable_document BEFORE UPDATE OR DELETE ON "Purchase" FOR EACH ROW EXECUTE FUNCTION invora_guard_document();
ALTER TABLE "SaleCostAllocation" ADD CONSTRAINT "CK_CostAllocation_ReturnBounds" CHECK ("Quantity" > 0 AND "ReturnedQuantity" BETWEEN 0 AND "Quantity" AND "RestoredQuantity" BETWEEN 0 AND "ReturnedQuantity");
ALTER TABLE "StockTransferItem" ADD CONSTRAINT "CK_Transfer_ReceiptBounds" CHECK ("Quantity" > 0 AND "ReceivedQuantity" BETWEEN 0 AND "Quantity");

""");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DROP TRIGGER immutable_history ON "LedgerEntry";
DROP TRIGGER immutable_history ON "InventoryMovement";
DROP TRIGGER immutable_history ON "OperationReceipt";
DROP TRIGGER immutable_history ON "Payment";
DROP TRIGGER immutable_history ON "PaymentAllocation";
DROP TRIGGER immutable_history ON "AllocationReversal";
DROP TRIGGER immutable_history ON "SaleReturn";
DROP TRIGGER immutable_history ON "SaleReturnItem";
DROP TRIGGER immutable_history ON "SaleNonCashSettlement";
DROP TRIGGER immutable_history ON "NonCashSettlementReversal";
DROP TRIGGER immutable_history ON "PurchaseReturn";
DROP TRIGGER immutable_history ON "PurchaseReturnItem";
DROP TRIGGER immutable_history ON "SaleItem";
DROP TRIGGER immutable_history ON "PurchaseItem";
DROP TRIGGER immutable_history ON "AccessAudits";
DROP TRIGGER immutable_history ON "DocumentFile";
DROP TRIGGER immutable_history ON "Expense";
DROP TRIGGER immutable_history ON "UnitIdentifier";
DROP TRIGGER immutable_history ON "PartyNote";
DROP TRIGGER immutable_history ON "DeviceInspection";
DROP TRIGGER immutable_history ON "DeviceAcquisition";
DROP FUNCTION invora_reject_history_change();
DROP TRIGGER immutable_document ON "Sale";
DROP TRIGGER immutable_document ON "Purchase";
DROP FUNCTION invora_guard_document();
ALTER TABLE "SaleCostAllocation" DROP CONSTRAINT "CK_CostAllocation_ReturnBounds";
ALTER TABLE "StockTransferItem" DROP CONSTRAINT "CK_Transfer_ReceiptBounds";

""");

        }
    }
}
