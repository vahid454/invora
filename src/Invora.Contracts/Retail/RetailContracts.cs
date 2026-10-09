namespace Invora.Contracts.Retail;
public sealed record TaxRequest(string Name, decimal Rate, decimal CessRate = 0);
public sealed record ProductRequest(string Name, string Brand, string Category, string Hsn, Guid TaxRateId, string Sku, string? Barcode, string Ram, string Storage, string Color, bool Serialized, decimal SellingPrice, decimal Mrp, int WarrantyMonths = 12, Guid? ProductModelId = null);
public sealed record PartyRequest(string Name, string Phone, string Email = "", string Address = "", string StateCode = "", string Gstin = "", decimal? CreditLimit = null, string AlternatePhone = "");
public sealed record DeviceCapture(string? Imei1, string? Imei2 = null, string? Serial = null, string? TrackingTag = null);
public sealed record PurchaseLineRequest(Guid ProductVariantId, int Quantity, decimal UnitCost, decimal Discount = 0, bool TaxInclusive = false, DeviceCapture[]? Devices = null);
public sealed record PurchaseRequest(Guid BranchId, Guid SupplierId, string SupplierInvoice, DateOnly BusinessDate, PurchaseLineRequest[] Items);
public sealed record SaleLineRequest(Guid ProductVariantId, int Quantity, decimal UnitPrice, Guid? StockUnitId = null, decimal Discount = 0, bool TaxInclusive = true);
public sealed record PaymentComponent(decimal Amount, int Method, string Reference = "", string Note = "");
public sealed record SaleRequest(Guid BranchId, Guid CustomerId, DateOnly BusinessDate, DateOnly? DueDate, bool Interstate, SaleLineRequest[] Items, PaymentComponent[] Payments, AcquisitionRequest? Exchange = null, string? SupplyStateCode = null);
public sealed record AllocationRequest(Guid? SaleId, Guid? PurchaseId, decimal Amount);
public sealed record PaymentRequest(Guid BranchId, Guid PartyId, decimal Amount, int Method, DateOnly BusinessDate, string Reference, string Note, AllocationRequest[] Allocations, bool Refund = false, Guid? InvoiceId = null, bool AutoAllocate = false);
public sealed record ReturnLineRequest(Guid SaleItemId, int Quantity, string Disposition);
public sealed record ReturnRequest(string Reason, DateOnly BusinessDate, ReturnLineRequest[] Items, bool Cancel = false);
public sealed record ReasonRequest(string Reason);
public sealed record TransferLineRequest(Guid ProductVariantId, int Quantity, Guid? StockUnitId = null);
public sealed record TransferRequest(Guid FromBranchId, Guid ToBranchId, string Note, TransferLineRequest[] Items);
public sealed record ExpenseRequest(Guid BranchId, string Category, string Description, decimal Amount, int Method, DateOnly BusinessDate);
public sealed record AdjustmentRequest(Guid BranchId, Guid ProductVariantId, int Quantity, decimal UnitCost, string Reason, DeviceCapture[]? Devices = null);
public sealed record AcquisitionRequest(Guid BranchId, Guid CustomerId, Guid? ProductVariantId, DeviceCapture? Device, decimal Amount, int Method, string Condition, string Notes, int? BatteryHealth, int WarrantyDays, Guid? ExchangeSaleId = null, OldDeviceDetails? OldDevice = null, string SellerName = "", string AadhaarLastFour = "");
public sealed record SettingsRequest(string LegalName = "", string Address = "", string Phone = "", string Email = "", string Gstin = "", string Pan = "", string StateCode = "23", bool GstRegistered = false, bool CompositionDealer = false, string BankName = "", string AccountHolder = "", string AccountNumber = "", string Ifsc = "", string BankBranch = "", string UpiId = "", string Declaration = "We declare that this invoice shows the actual price of the goods described and that all particulars are true and correct.", string Jurisdiction = "", string WarrantyTerms = WarrantyPolicy.Terms, string ReturnPolicy = "Returns subject to inspection and store policy.");
public sealed record NoteRequest(Guid BranchId, string Text);
public sealed record DocumentView(Guid Id, string Number, string Status, decimal Total, Guid? PartyId = null, string Reference = "", string PartyName = "", string Phone = "", string AlternatePhone = "", DateOnly? BusinessDate = null);
public sealed record TaxLine(decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst, decimal Cess, decimal Total);
public sealed record InvoiceLine(string Description, string Hsn, string[] Identifiers, int Quantity, decimal UnitPrice, decimal Discount, decimal Rate, decimal CessRate, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst, decimal Cess, decimal Total, int WarrantyMonths, string Condition = "", string WarrantyText = "");
public sealed record InvoiceSnapshot(string Number, DateOnly Date, DateOnly? DueDate, string SellerName, SettingsRequest Seller, string CustomerName, string CustomerPhone, string CustomerAddress, string CustomerGstin, string CustomerStateCode, InvoiceLine[] Lines, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst, decimal Cess, decimal Total, bool Interstate, string DocumentTitle, string Reference = "", string SupplyStateCode = "", DateTimeOffset? IssuedAt = null, string CustomerAlternatePhone = "", string OriginalInvoiceNumber = "", DateOnly? OriginalInvoiceDate = null);
public sealed record InventoryView(Guid Id, Guid ProductVariantId, Guid BranchId, string Description, string Sku, string Condition, string Status, string[] Identifiers, decimal? Cost, decimal SellingPrice, DateTimeOffset ReceivedAtUtc);
public sealed record ProductView(Guid Id, string Name, string Brand, string Category, string Hsn, string Sku, string? Barcode, string Ram, string Storage, string Color, bool Serialized, decimal SellingPrice, decimal Mrp, Guid TaxRateId, decimal Rate, decimal CessRate, int WarrantyMonths, Guid ProductModelId = default, bool RequiresImei = false);

public sealed record PurchaseReturnLine(Guid PurchaseItemId, int Quantity, Guid? StockUnitId = null);
public sealed record PurchaseReturnRequest(string Reason, string SupplierCreditReference, DateOnly BusinessDate, PurchaseReturnLine[] Items);

public sealed record ImportRequest(string Kind, string Csv, Guid? BranchId = null);
public sealed record FileUploadRequest(Guid? PurchaseId, Guid? ExpenseId, string Name, string Base64);
public sealed record PaymentReceipt(string Number, DateOnly Date, string SellerName, string SellerAddress, string PartyName, string PartyPhone, decimal Amount, string Direction, string Method, string Reference, string Note);
public sealed record TransferReceiptLine(Guid ItemId, int Quantity);
public sealed record TransferReceiptRequest(TransferReceiptLine[]? Items = null);

public sealed record ProductWithStockRequest(ProductRequest Product, Guid BranchId, int Quantity, decimal UnitCost, string Reason, DeviceCapture[]? Devices = null, DeviceStockLine[]? DeviceStock = null);
public sealed record LenDenRequest(Guid BranchId, Guid CustomerId, string Direction, decimal Amount, int Method, DateOnly BusinessDate, string Reference, string Note);

public sealed record BrandRequest(string Name);
public sealed record DeviceStockLine(DeviceCapture Device, string Ram, string Storage, string Color, decimal UnitCost, decimal SellingPrice, decimal Mrp);
public sealed record DeviceStockBatchRequest(Guid BranchId, Guid ProductVariantId, string Reason, DeviceStockLine[] Devices);

public sealed record CategoryRequest(string Name, bool RequiresImei = false);
public sealed record CategoryView(string Name, bool RequiresImei);

public sealed record OldDeviceDetails(string Name, string Brand = "", string Ram = "", string Storage = "", string Color = "");
public sealed record ProductPurchaseRequest(ProductRequest Product, int Quantity, decimal UnitCost, DeviceStockLine[]? DeviceStock = null);
public sealed record PreparedPurchaseLine(ProductView Product, int Quantity, decimal UnitCost, DeviceCapture[] Devices);
public sealed record FollowUpRequest(Guid BranchId, Guid CustomerId, string Kind, string Note, DateOnly? PromiseDate = null, DateOnly? NextContactDate = null, decimal? Amount = null);
public sealed record StatementRow(DateOnly Date, string Kind, string Note, decimal Debit, decimal Credit, decimal RunningBalance);
public sealed record CustomerStatement(string ShopName, string ShopAddress, string CustomerName, string Phone, string AlternatePhone, DateOnly From, DateOnly To, decimal OpeningBalance, decimal ClosingBalance, decimal TradeBalance, decimal IndependentBalance, StatementRow[] Rows);

public sealed record DeviceEditState(string Ram, string Storage, string Color, string Condition, int? BatteryHealth, string Notes);
public sealed record DeviceCorrectionRequest(Guid BranchId, string Revision, string Ram, string Storage, string Color, string Condition, int? BatteryHealth, string Notes, string Reason);
