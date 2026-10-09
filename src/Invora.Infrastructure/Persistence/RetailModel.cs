using System.Linq.Expressions;
using Invora.Domain.Modules.Businesses;
using Invora.Domain.Modules.Retail;
using Invora.Domain.Modules.Licensing;
using Microsoft.EntityFrameworkCore;
namespace Invora.Infrastructure.Persistence;
internal static class RetailModel
{
    private static void Fk<T,U>(ModelBuilder m,Expression<Func<T,object?>> key) where T:class where U:class => m.Entity<T>().HasOne<U>().WithMany().HasForeignKey(key).OnDelete(DeleteBehavior.Restrict);
    public static void Configure(ModelBuilder m)
    {
        var types=new[]{typeof(DeviceCorrection),typeof(BusinessLicense),typeof(PaymentFollowUp),typeof(ProductCategory),typeof(Brand),typeof(ImportJob),typeof(TaxRate),typeof(ProductModel),typeof(ProductVariant),typeof(Party),typeof(PartyNote),typeof(StockUnit),typeof(UnitIdentifier),typeof(StockBalance),typeof(StockCostLayer),typeof(InventoryMovement),typeof(Purchase),typeof(PurchaseItem),typeof(Sale),typeof(SaleItem),typeof(SaleCostAllocation),typeof(Payment),typeof(PaymentAllocation),typeof(AllocationReversal),typeof(LedgerEntry),typeof(SaleReturn),typeof(SaleReturnItem),typeof(StockTransfer),typeof(StockTransferItem),typeof(Expense),typeof(DocumentSequence),typeof(OperationReceipt),typeof(BusinessSettings),typeof(DocumentFile),typeof(DeviceInspection),typeof(DeviceAcquisition),typeof(SaleNonCashSettlement),typeof(PurchaseReturn),typeof(PurchaseReturnItem),typeof(NonCashSettlementReversal)};
        Fk<DeviceCorrection,StockUnit>(m,x=>x.StockUnitId);Fk<DeviceCorrection,Branch>(m,x=>x.BranchId);m.Entity<DeviceCorrection>().HasIndex(x=>new{x.StockUnitId,x.CreatedAtUtc});
        Fk<BusinessLicense,BusinessProfile>(m,x=>x.BusinessId);
        m.Entity<BusinessLicense>().HasIndex(x=>x.CreatedAtUtc);
        foreach(var type in types){var b=m.Entity(type);b.HasKey("Id");b.Property("Version").IsConcurrencyToken();}
        Fk<PaymentFollowUp,Branch>(m,x=>x.BranchId);Fk<PaymentFollowUp,Party>(m,x=>x.CustomerId);m.Entity<PaymentFollowUp>().HasIndex(x=>new{x.BranchId,x.CustomerId,x.CreatedAtUtc});
        Fk<ImportJob,Branch>(m,x=>x.BranchId);Fk<ProductModel,TaxRate>(m,x=>x.TaxRateId);Fk<ProductVariant,ProductModel>(m,x=>x.ProductModelId);
        Fk<PartyNote,Party>(m,x=>x.PartyId);Fk<PartyNote,Branch>(m,x=>x.BranchId);
        Fk<StockUnit,Branch>(m,x=>x.BranchId);Fk<StockUnit,ProductVariant>(m,x=>x.ProductVariantId);Fk<StockUnit,PurchaseItem>(m,x=>x.PurchaseItemId);Fk<StockUnit,SaleItem>(m,x=>x.ActiveSaleItemId);
        Fk<UnitIdentifier,StockUnit>(m,x=>x.StockUnitId);Fk<StockBalance,Branch>(m,x=>x.BranchId);Fk<StockBalance,ProductVariant>(m,x=>x.ProductVariantId);
        Fk<StockCostLayer,Branch>(m,x=>x.BranchId);Fk<StockCostLayer,ProductVariant>(m,x=>x.ProductVariantId);Fk<StockCostLayer,PurchaseItem>(m,x=>x.PurchaseItemId);Fk<StockCostLayer,StockCostLayer>(m,x=>x.OriginLayerId);
        Fk<Purchase,Branch>(m,x=>x.BranchId);Fk<Purchase,Party>(m,x=>x.SupplierId);Fk<PurchaseItem,Purchase>(m,x=>x.PurchaseId);Fk<PurchaseItem,ProductVariant>(m,x=>x.ProductVariantId);
        Fk<Sale,Branch>(m,x=>x.BranchId);Fk<Sale,Party>(m,x=>x.CustomerId);Fk<SaleItem,Sale>(m,x=>x.SaleId);Fk<SaleItem,ProductVariant>(m,x=>x.ProductVariantId);Fk<SaleItem,StockUnit>(m,x=>x.StockUnitId);
        Fk<SaleCostAllocation,SaleItem>(m,x=>x.SaleItemId);Fk<SaleCostAllocation,StockCostLayer>(m,x=>x.StockCostLayerId);
        Fk<Payment,Branch>(m,x=>x.BranchId);Fk<Payment,Party>(m,x=>x.PartyId);Fk<Payment,Payment>(m,x=>x.ReversesPaymentId);
        Fk<PaymentAllocation,Payment>(m,x=>x.PaymentId);Fk<PaymentAllocation,Sale>(m,x=>x.SaleId);Fk<PaymentAllocation,Purchase>(m,x=>x.PurchaseId);Fk<AllocationReversal,PaymentAllocation>(m,x=>x.PaymentAllocationId);
        Fk<LedgerEntry,Branch>(m,x=>x.BranchId);Fk<LedgerEntry,Party>(m,x=>x.PartyId);Fk<LedgerEntry,Sale>(m,x=>x.SaleId);Fk<LedgerEntry,Purchase>(m,x=>x.PurchaseId);Fk<LedgerEntry,Payment>(m,x=>x.PaymentId);Fk<LedgerEntry,SaleReturn>(m,x=>x.SaleReturnId);Fk<LedgerEntry,LedgerEntry>(m,x=>x.ReversesEntryId);
        Fk<SaleReturn,Sale>(m,x=>x.SaleId);Fk<SaleReturnItem,SaleReturn>(m,x=>x.SaleReturnId);Fk<SaleReturnItem,SaleItem>(m,x=>x.SaleItemId);
        Fk<StockTransfer,Branch>(m,x=>x.FromBranchId);Fk<StockTransfer,Branch>(m,x=>x.ToBranchId);Fk<StockTransferItem,StockTransfer>(m,x=>x.StockTransferId);Fk<StockTransferItem,ProductVariant>(m,x=>x.ProductVariantId);Fk<StockTransferItem,StockUnit>(m,x=>x.StockUnitId);Fk<StockTransferItem,StockCostLayer>(m,x=>x.StockCostLayerId);
        Fk<InventoryMovement,Branch>(m,x=>x.BranchId);Fk<InventoryMovement,ProductVariant>(m,x=>x.ProductVariantId);Fk<InventoryMovement,StockUnit>(m,x=>x.StockUnitId);Fk<InventoryMovement,Purchase>(m,x=>x.PurchaseId);Fk<InventoryMovement,Sale>(m,x=>x.SaleId);Fk<InventoryMovement,SaleReturn>(m,x=>x.SaleReturnId);Fk<InventoryMovement,StockTransfer>(m,x=>x.TransferId);
        Fk<Expense,Branch>(m,x=>x.BranchId);Fk<Expense,Expense>(m,x=>x.ReversesExpenseId);Fk<DocumentSequence,Branch>(m,x=>x.BranchId);Fk<DocumentFile,Branch>(m,x=>x.BranchId);Fk<DocumentFile,Purchase>(m,x=>x.PurchaseId);Fk<DocumentFile,Expense>(m,x=>x.ExpenseId);
        Fk<DeviceInspection,StockUnit>(m,x=>x.StockUnitId);Fk<DeviceAcquisition,StockUnit>(m,x=>x.StockUnitId);Fk<DeviceAcquisition,Branch>(m,x=>x.BranchId);Fk<DeviceAcquisition,Party>(m,x=>x.CustomerId);Fk<DeviceAcquisition,Sale>(m,x=>x.ExchangeSaleId);Fk<DeviceAcquisition,Payment>(m,x=>x.PaymentId);
        Fk<NonCashSettlementReversal,SaleNonCashSettlement>(m,x=>x.SaleNonCashSettlementId);Fk<SaleNonCashSettlement,Sale>(m,x=>x.SaleId);Fk<SaleNonCashSettlement,DeviceAcquisition>(m,x=>x.DeviceAcquisitionId);
        Fk<LedgerEntry,DeviceAcquisition>(m,x=>x.DeviceAcquisitionId);Fk<LedgerEntry,PurchaseReturn>(m,x=>x.PurchaseReturnId);
        Fk<PurchaseReturn,Purchase>(m,x=>x.PurchaseId);Fk<PurchaseReturnItem,PurchaseReturn>(m,x=>x.PurchaseReturnId);Fk<PurchaseReturnItem,PurchaseItem>(m,x=>x.PurchaseItemId);Fk<PurchaseReturnItem,StockUnit>(m,x=>x.StockUnitId);
        m.Entity<SaleNonCashSettlement>().HasIndex(x=>x.DeviceAcquisitionId).IsUnique();
        m.Entity<Purchase>().HasIndex(x=>new{x.SupplierId,x.SupplierInvoice,x.FinancialYear}).IsUnique().HasFilter("\"Status\" = 'Completed'");
        m.Entity<ProductCategory>().HasIndex(x=>x.NormalizedName).IsUnique();
        m.Entity<Brand>().HasIndex(x=>x.NormalizedName).IsUnique();
        m.Entity<ProductVariant>().HasIndex(x=>x.Sku).IsUnique();m.Entity<ProductVariant>().HasIndex(x=>x.Barcode).IsUnique().HasFilter("\"Barcode\" IS NOT NULL");
        m.Entity<UnitIdentifier>().HasIndex(x=>new{x.Kind,x.Value}).IsUnique();m.Entity<UnitIdentifier>().HasIndex(x=>new{x.StockUnitId,x.Slot}).IsUnique();
        m.Entity<StockBalance>().HasIndex(x=>new{x.BranchId,x.ProductVariantId}).IsUnique();
        m.Entity<StockUnit>().HasIndex(x=>new{x.BranchId,x.Status,x.ProductVariantId});
        m.Entity<StockCostLayer>().HasIndex(x=>new{x.BranchId,x.ProductVariantId,x.CreatedAtUtc});
        m.Entity<Sale>().HasIndex(x=>x.Number).IsUnique().HasFilter("\"Number\" <> ''");m.Entity<Purchase>().HasIndex(x=>x.Number).IsUnique().HasFilter("\"Number\" <> ''");
        m.Entity<Sale>().HasIndex(x=>new{x.BranchId,x.BusinessDate});m.Entity<Purchase>().HasIndex(x=>new{x.BranchId,x.BusinessDate});
        m.Entity<Party>().HasIndex(x=>new{x.Kind,x.Phone});m.Entity<LedgerEntry>().HasIndex(x=>new{x.BranchId,x.PartyId,x.BusinessDate,x.CreatedAtUtc});
        m.Entity<Payment>().HasIndex(x=>x.ReversesPaymentId).IsUnique().HasFilter("\"ReversesPaymentId\" IS NOT NULL");
        m.Entity<Expense>().HasIndex(x=>x.ReversesExpenseId).IsUnique().HasFilter("\"ReversesExpenseId\" IS NOT NULL");
        m.Entity<LedgerEntry>().HasIndex(x=>x.ReversesEntryId).IsUnique().HasFilter("\"ReversesEntryId\" IS NOT NULL");
        m.Entity<DocumentSequence>().HasIndex(x=>new{x.BranchId,x.Kind,x.FinancialYear}).IsUnique();
        m.Entity<OperationReceipt>().HasIndex(x=>new{x.UserId,x.Operation,x.Key}).IsUnique();
        m.Entity<BusinessSettings>().HasIndex(x=>x.SingletonKey).IsUnique();m.Entity<BusinessSettings>().ToTable("BusinessSettings",t=>t.HasCheckConstraint("CK_Settings_Singleton","\"SingletonKey\" = 1"));
        m.Entity<PaymentAllocation>().ToTable("PaymentAllocation",t=>t.HasCheckConstraint("CK_Allocation_Target","(\"SaleId\" IS NULL) <> (\"PurchaseId\" IS NULL) AND \"Amount\" > 0"));
        m.Entity<LedgerEntry>().ToTable("LedgerEntry",t=>t.HasCheckConstraint("CK_Ledger_Amounts","\"Debit\" >= 0 AND \"Credit\" >= 0 AND ((\"Debit\" > 0 AND \"Credit\" = 0) OR (\"Debit\" = 0 AND \"Credit\" > 0))"));
        m.Entity<StockBalance>().ToTable("StockBalance",t=>t.HasCheckConstraint("CK_Balance_Positive","\"Quantity\" >= 0"));
        m.Entity<StockCostLayer>().ToTable("StockCostLayer",t=>t.HasCheckConstraint("CK_Layer_Quantity","\"RemainingQuantity\" >= 0 AND \"RemainingQuantity\" <= \"ReceivedQuantity\" AND \"UnitCost\" >= 0"));
        m.Entity<Payment>().ToTable("Payment",t=>t.HasCheckConstraint("CK_Payment_Amount","\"Amount\" > 0 AND \"Direction\" IN ('In','Out')"));
        foreach(var entity in m.Model.GetEntityTypes())foreach(var prop in entity.GetProperties())
        {
            if(prop.ClrType==typeof(decimal) || prop.ClrType==typeof(decimal?))prop.SetColumnType(prop.Name is "Rate" or "CessRate"?"numeric(7,4)":"numeric(18,2)");
            if(prop.ClrType==typeof(string)){prop.SetMaxLength(prop.Name.EndsWith("Json",StringComparison.Ordinal)?null:2000);if(prop.Name.EndsWith("Json",StringComparison.Ordinal))prop.SetColumnType("jsonb");}
        }
        m.Entity<BusinessLicense>().Property(x=>x.Token).HasMaxLength(12000);
    }
}
