using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetailCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SingletonKey = table.Column<int>(type: "integer", nullable: false),
                    Json = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessSettings", x => x.Id);
                    table.CheckConstraint("CK_Settings_Singleton", "\"SingletonKey\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "DocumentSequence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    FinancialYear = table.Column<int>(type: "integer", nullable: false),
                    LastNumber = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentSequence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentSequence_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Expense",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReversesExpenseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expense", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Expense_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expense_Expense_ReversesExpenseId",
                        column: x => x.ReversesExpenseId,
                        principalTable: "Expense",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OperationReceipt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Operation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationReceipt", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Party",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Phone = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Email = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Address = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    StateCode = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Gstin = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreditLimit = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Party", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockTransfer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FromBranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToBranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransfer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTransfer_Branches_FromBranchId",
                        column: x => x.FromBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfer_Branches_ToBranchId",
                        column: x => x.ToBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxRate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(7,4)", nullable: false),
                    CessRate = table.Column<decimal>(type: "numeric(7,4)", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PartyNote",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyNote", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartyNote_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartyNote_Party_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Party",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Direction = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ReversesPaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payment", x => x.Id);
                    table.CheckConstraint("CK_Payment_Amount", "\"Amount\" > 0 AND \"Direction\" IN ('In','Out')");
                    table.ForeignKey(
                        name: "FK_Payment_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payment_Party_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Party",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payment_Payment_ReversesPaymentId",
                        column: x => x.ReversesPaymentId,
                        principalTable: "Payment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Purchase",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierInvoice = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Number = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RequestJson = table.Column<string>(type: "jsonb", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Purchase", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Purchase_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Purchase_Party_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Party",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sale",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RequestJson = table.Column<string>(type: "jsonb", nullable: false),
                    InvoiceJson = table.Column<string>(type: "jsonb", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Taxable = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Cgst = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Sgst = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Igst = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Cess = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sale", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sale_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sale_Party_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Party",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductModel",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Brand = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Category = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Hsn = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    TaxRateId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarrantyMonths = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductModel", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductModel_TaxRate_TaxRateId",
                        column: x => x.TaxRateId,
                        principalTable: "TaxRate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentFile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    PurchaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpenseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentFile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentFile_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentFile_Expense_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expense",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentFile_Purchase_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "Purchase",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentAllocation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: true),
                    PurchaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAllocation", x => x.Id);
                    table.CheckConstraint("CK_Allocation_Target", "(\"SaleId\" IS NULL) <> (\"PurchaseId\" IS NULL) AND \"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_PaymentAllocation_Payment_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentAllocation_Purchase_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "Purchase",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentAllocation_Sale_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sale",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaleReturn",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceJson = table.Column<string>(type: "jsonb", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Credit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Cancellation = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleReturn", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleReturn_Sale_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sale",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Barcode = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Ram = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Storage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Color = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Serialized = table.Column<bool>(type: "boolean", nullable: false),
                    SellingPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Mrp = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariant", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductVariant_ProductModel_ProductModelId",
                        column: x => x.ProductModelId,
                        principalTable: "ProductModel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AllocationReversal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentAllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllocationReversal", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllocationReversal_PaymentAllocation_PaymentAllocationId",
                        column: x => x.PaymentAllocationId,
                        principalTable: "PaymentAllocation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: true),
                    PurchaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SaleReturnId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversesEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Debit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Credit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntry", x => x.Id);
                    table.CheckConstraint("CK_Ledger_Amounts", "\"Debit\" >= 0 AND \"Credit\" >= 0 AND ((\"Debit\" > 0 AND \"Credit\" = 0) OR (\"Debit\" = 0 AND \"Credit\" > 0))");
                    table.ForeignKey(
                        name: "FK_LedgerEntry_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntry_LedgerEntry_ReversesEntryId",
                        column: x => x.ReversesEntryId,
                        principalTable: "LedgerEntry",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntry_Party_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Party",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntry_Payment_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntry_Purchase_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "Purchase",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntry_SaleReturn_SaleReturnId",
                        column: x => x.SaleReturnId,
                        principalTable: "SaleReturn",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntry_Sale_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sale",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Taxable = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Tax = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseItem_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseItem_Purchase_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "Purchase",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockBalance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockBalance", x => x.Id);
                    table.CheckConstraint("CK_Balance_Positive", "\"Quantity\" >= 0");
                    table.ForeignKey(
                        name: "FK_StockBalance_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockBalance_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockCostLayer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    OriginLayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedQuantity = table.Column<int>(type: "integer", nullable: false),
                    RemainingQuantity = table.Column<int>(type: "integer", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockCostLayer", x => x.Id);
                    table.CheckConstraint("CK_Layer_Quantity", "\"RemainingQuantity\" >= 0 AND \"RemainingQuantity\" <= \"ReceivedQuantity\" AND \"UnitCost\" >= 0");
                    table.ForeignKey(
                        name: "FK_StockCostLayer_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCostLayer_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCostLayer_PurchaseItem_PurchaseItemId",
                        column: x => x.PurchaseItemId,
                        principalTable: "PurchaseItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCostLayer_StockCostLayer_OriginLayerId",
                        column: x => x.OriginLayerId,
                        principalTable: "StockCostLayer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeviceAcquisition",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ExchangeSaleId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceAcquisition", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceAcquisition_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceAcquisition_Party_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Party",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceAcquisition_Payment_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceAcquisition_Sale_ExchangeSaleId",
                        column: x => x.ExchangeSaleId,
                        principalTable: "Sale",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeviceInspection",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StockUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    BatteryHealth = table.Column<int>(type: "integer", nullable: true),
                    ChecksJson = table.Column<string>(type: "jsonb", nullable: false),
                    WarrantyDays = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceInspection", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InventoryMovement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    PurchaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: true),
                    SaleReturnId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransferId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryMovement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryMovement_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovement_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovement_Purchase_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "Purchase",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovement_SaleReturn_SaleReturnId",
                        column: x => x.SaleReturnId,
                        principalTable: "SaleReturn",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovement_Sale_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sale",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovement_StockTransfer_TransferId",
                        column: x => x.TransferId,
                        principalTable: "StockTransfer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaleCostAllocation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockCostLayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    RestoredQuantity = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleCostAllocation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleCostAllocation_StockCostLayer_StockCostLayerId",
                        column: x => x.StockCostLayerId,
                        principalTable: "StockCostLayer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaleItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Hsn = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IdentifiersJson = table.Column<string>(type: "jsonb", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(7,4)", nullable: false),
                    CessRate = table.Column<decimal>(type: "numeric(7,4)", nullable: false),
                    Taxable = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Cgst = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Sgst = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Igst = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Cess = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleItem_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleItem_Sale_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sale",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaleReturnItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Taxable = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Cgst = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Sgst = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Igst = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Cess = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    SaleReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Disposition = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Credit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleReturnItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleReturnItem_SaleItem_SaleItemId",
                        column: x => x.SaleItemId,
                        principalTable: "SaleItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleReturnItem_SaleReturn_SaleReturnId",
                        column: x => x.SaleReturnId,
                        principalTable: "SaleReturn",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockUnit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Cost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Condition = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Source = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ActiveSaleItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockUnit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockUnit_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockUnit_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockUnit_PurchaseItem_PurchaseItemId",
                        column: x => x.PurchaseItemId,
                        principalTable: "PurchaseItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockUnit_SaleItem_ActiveSaleItemId",
                        column: x => x.ActiveSaleItemId,
                        principalTable: "SaleItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockTransferItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedQuantity = table.Column<int>(type: "integer", nullable: false),
                    StockTransferId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    StockCostLayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransferItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTransferItem_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransferItem_StockCostLayer_StockCostLayerId",
                        column: x => x.StockCostLayerId,
                        principalTable: "StockCostLayer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransferItem_StockTransfer_StockTransferId",
                        column: x => x.StockTransferId,
                        principalTable: "StockTransfer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransferItem_StockUnit_StockUnitId",
                        column: x => x.StockUnitId,
                        principalTable: "StockUnit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UnitIdentifier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StockUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Slot = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitIdentifier", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnitIdentifier_StockUnit_StockUnitId",
                        column: x => x.StockUnitId,
                        principalTable: "StockUnit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AllocationReversal_PaymentAllocationId",
                table: "AllocationReversal",
                column: "PaymentAllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessSettings_SingletonKey",
                table: "BusinessSettings",
                column: "SingletonKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceAcquisition_BranchId",
                table: "DeviceAcquisition",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceAcquisition_CustomerId",
                table: "DeviceAcquisition",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceAcquisition_ExchangeSaleId",
                table: "DeviceAcquisition",
                column: "ExchangeSaleId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceAcquisition_PaymentId",
                table: "DeviceAcquisition",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceAcquisition_StockUnitId",
                table: "DeviceAcquisition",
                column: "StockUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceInspection_StockUnitId",
                table: "DeviceInspection",
                column: "StockUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFile_BranchId",
                table: "DocumentFile",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFile_ExpenseId",
                table: "DocumentFile",
                column: "ExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFile_PurchaseId",
                table: "DocumentFile",
                column: "PurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSequence_BranchId_Kind_FinancialYear",
                table: "DocumentSequence",
                columns: new[] { "BranchId", "Kind", "FinancialYear" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expense_BranchId",
                table: "Expense",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Expense_ReversesExpenseId",
                table: "Expense",
                column: "ReversesExpenseId",
                unique: true,
                filter: "\"ReversesExpenseId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovement_BranchId",
                table: "InventoryMovement",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovement_ProductVariantId",
                table: "InventoryMovement",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovement_PurchaseId",
                table: "InventoryMovement",
                column: "PurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovement_SaleId",
                table: "InventoryMovement",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovement_SaleReturnId",
                table: "InventoryMovement",
                column: "SaleReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovement_StockUnitId",
                table: "InventoryMovement",
                column: "StockUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovement_TransferId",
                table: "InventoryMovement",
                column: "TransferId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntry_BranchId_PartyId_BusinessDate_CreatedAtUtc",
                table: "LedgerEntry",
                columns: new[] { "BranchId", "PartyId", "BusinessDate", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntry_PartyId",
                table: "LedgerEntry",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntry_PaymentId",
                table: "LedgerEntry",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntry_PurchaseId",
                table: "LedgerEntry",
                column: "PurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntry_ReversesEntryId",
                table: "LedgerEntry",
                column: "ReversesEntryId",
                unique: true,
                filter: "\"ReversesEntryId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntry_SaleId",
                table: "LedgerEntry",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntry_SaleReturnId",
                table: "LedgerEntry",
                column: "SaleReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationReceipt_UserId_Operation_Key",
                table: "OperationReceipt",
                columns: new[] { "UserId", "Operation", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Party_Kind_Phone",
                table: "Party",
                columns: new[] { "Kind", "Phone" });

            migrationBuilder.CreateIndex(
                name: "IX_PartyNote_BranchId",
                table: "PartyNote",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyNote_PartyId",
                table: "PartyNote",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_Payment_BranchId",
                table: "Payment",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Payment_PartyId",
                table: "Payment",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_Payment_ReversesPaymentId",
                table: "Payment",
                column: "ReversesPaymentId",
                unique: true,
                filter: "\"ReversesPaymentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocation_PaymentId",
                table: "PaymentAllocation",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocation_PurchaseId",
                table: "PaymentAllocation",
                column: "PurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocation_SaleId",
                table: "PaymentAllocation",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductModel_TaxRateId",
                table: "ProductModel",
                column: "TaxRateId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_Barcode",
                table: "ProductVariant",
                column: "Barcode",
                unique: true,
                filter: "\"Barcode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_ProductModelId",
                table: "ProductVariant",
                column: "ProductModelId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_Sku",
                table: "ProductVariant",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Purchase_BranchId_BusinessDate",
                table: "Purchase",
                columns: new[] { "BranchId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Purchase_Number",
                table: "Purchase",
                column: "Number",
                unique: true,
                filter: "\"Number\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_Purchase_SupplierId",
                table: "Purchase",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseItem_ProductVariantId",
                table: "PurchaseItem",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseItem_PurchaseId",
                table: "PurchaseItem",
                column: "PurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Sale_BranchId_BusinessDate",
                table: "Sale",
                columns: new[] { "BranchId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Sale_CustomerId",
                table: "Sale",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Sale_Number",
                table: "Sale",
                column: "Number",
                unique: true,
                filter: "\"Number\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_SaleCostAllocation_SaleItemId",
                table: "SaleCostAllocation",
                column: "SaleItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleCostAllocation_StockCostLayerId",
                table: "SaleCostAllocation",
                column: "StockCostLayerId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleItem_ProductVariantId",
                table: "SaleItem",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleItem_SaleId",
                table: "SaleItem",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleItem_StockUnitId",
                table: "SaleItem",
                column: "StockUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturn_SaleId",
                table: "SaleReturn",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturnItem_SaleItemId",
                table: "SaleReturnItem",
                column: "SaleItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturnItem_SaleReturnId",
                table: "SaleReturnItem",
                column: "SaleReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_StockBalance_BranchId_ProductVariantId",
                table: "StockBalance",
                columns: new[] { "BranchId", "ProductVariantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockBalance_ProductVariantId",
                table: "StockBalance",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCostLayer_BranchId_ProductVariantId_CreatedAtUtc",
                table: "StockCostLayer",
                columns: new[] { "BranchId", "ProductVariantId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StockCostLayer_OriginLayerId",
                table: "StockCostLayer",
                column: "OriginLayerId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCostLayer_ProductVariantId",
                table: "StockCostLayer",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCostLayer_PurchaseItemId",
                table: "StockCostLayer",
                column: "PurchaseItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfer_FromBranchId",
                table: "StockTransfer",
                column: "FromBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfer_ToBranchId",
                table: "StockTransfer",
                column: "ToBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItem_ProductVariantId",
                table: "StockTransferItem",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItem_StockCostLayerId",
                table: "StockTransferItem",
                column: "StockCostLayerId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItem_StockTransferId",
                table: "StockTransferItem",
                column: "StockTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItem_StockUnitId",
                table: "StockTransferItem",
                column: "StockUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StockUnit_ActiveSaleItemId",
                table: "StockUnit",
                column: "ActiveSaleItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StockUnit_BranchId_Status_ProductVariantId",
                table: "StockUnit",
                columns: new[] { "BranchId", "Status", "ProductVariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockUnit_ProductVariantId",
                table: "StockUnit",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockUnit_PurchaseItemId",
                table: "StockUnit",
                column: "PurchaseItemId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitIdentifier_Kind_Value",
                table: "UnitIdentifier",
                columns: new[] { "Kind", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitIdentifier_StockUnitId_Slot",
                table: "UnitIdentifier",
                columns: new[] { "StockUnitId", "Slot" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DeviceAcquisition_StockUnit_StockUnitId",
                table: "DeviceAcquisition",
                column: "StockUnitId",
                principalTable: "StockUnit",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeviceInspection_StockUnit_StockUnitId",
                table: "DeviceInspection",
                column: "StockUnitId",
                principalTable: "StockUnit",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovement_StockUnit_StockUnitId",
                table: "InventoryMovement",
                column: "StockUnitId",
                principalTable: "StockUnit",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SaleCostAllocation_SaleItem_SaleItemId",
                table: "SaleCostAllocation",
                column: "SaleItemId",
                principalTable: "SaleItem",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SaleItem_StockUnit_StockUnitId",
                table: "SaleItem",
                column: "StockUnitId",
                principalTable: "StockUnit",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Purchase_Party_SupplierId",
                table: "Purchase");

            migrationBuilder.DropForeignKey(
                name: "FK_Sale_Party_CustomerId",
                table: "Sale");

            migrationBuilder.DropForeignKey(
                name: "FK_SaleItem_Sale_SaleId",
                table: "SaleItem");

            migrationBuilder.DropForeignKey(
                name: "FK_SaleItem_StockUnit_StockUnitId",
                table: "SaleItem");

            migrationBuilder.DropTable(
                name: "AllocationReversal");

            migrationBuilder.DropTable(
                name: "BusinessSettings");

            migrationBuilder.DropTable(
                name: "DeviceAcquisition");

            migrationBuilder.DropTable(
                name: "DeviceInspection");

            migrationBuilder.DropTable(
                name: "DocumentFile");

            migrationBuilder.DropTable(
                name: "DocumentSequence");

            migrationBuilder.DropTable(
                name: "InventoryMovement");

            migrationBuilder.DropTable(
                name: "LedgerEntry");

            migrationBuilder.DropTable(
                name: "OperationReceipt");

            migrationBuilder.DropTable(
                name: "PartyNote");

            migrationBuilder.DropTable(
                name: "SaleCostAllocation");

            migrationBuilder.DropTable(
                name: "SaleReturnItem");

            migrationBuilder.DropTable(
                name: "StockBalance");

            migrationBuilder.DropTable(
                name: "StockTransferItem");

            migrationBuilder.DropTable(
                name: "UnitIdentifier");

            migrationBuilder.DropTable(
                name: "PaymentAllocation");

            migrationBuilder.DropTable(
                name: "Expense");

            migrationBuilder.DropTable(
                name: "SaleReturn");

            migrationBuilder.DropTable(
                name: "StockCostLayer");

            migrationBuilder.DropTable(
                name: "StockTransfer");

            migrationBuilder.DropTable(
                name: "Payment");

            migrationBuilder.DropTable(
                name: "Party");

            migrationBuilder.DropTable(
                name: "Sale");

            migrationBuilder.DropTable(
                name: "StockUnit");

            migrationBuilder.DropTable(
                name: "PurchaseItem");

            migrationBuilder.DropTable(
                name: "SaleItem");

            migrationBuilder.DropTable(
                name: "Purchase");

            migrationBuilder.DropTable(
                name: "ProductVariant");

            migrationBuilder.DropTable(
                name: "ProductModel");

            migrationBuilder.DropTable(
                name: "TaxRate");
        }
    }
}
