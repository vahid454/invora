using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReturnsAndExchange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReturnedQuantity",
                table: "SaleCostAllocation",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid[]>(
                name: "BranchIds",
                table: "OperationReceipt",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "Permissions",
                table: "OperationReceipt",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "DeviceAcquisitionId",
                table: "LedgerEntry",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseReturnId",
                table: "LedgerEntry",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PurchaseReturn",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SupplierCreditReference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Credit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReturn", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseReturn_Purchase_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "Purchase",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaleNonCashSettlement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceAcquisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleNonCashSettlement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleNonCashSettlement_DeviceAcquisition_DeviceAcquisitionId",
                        column: x => x.DeviceAcquisitionId,
                        principalTable: "DeviceAcquisition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleNonCashSettlement_Sale_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sale",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReturnItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Credit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReturnItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnItem_PurchaseItem_PurchaseItemId",
                        column: x => x.PurchaseItemId,
                        principalTable: "PurchaseItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnItem_PurchaseReturn_PurchaseReturnId",
                        column: x => x.PurchaseReturnId,
                        principalTable: "PurchaseReturn",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnItem_StockUnit_StockUnitId",
                        column: x => x.StockUnitId,
                        principalTable: "StockUnit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NonCashSettlementReversal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleNonCashSettlementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NonCashSettlementReversal", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NonCashSettlementReversal_SaleNonCashSettlement_SaleNonCash~",
                        column: x => x.SaleNonCashSettlementId,
                        principalTable: "SaleNonCashSettlement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntry_DeviceAcquisitionId",
                table: "LedgerEntry",
                column: "DeviceAcquisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntry_PurchaseReturnId",
                table: "LedgerEntry",
                column: "PurchaseReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_NonCashSettlementReversal_SaleNonCashSettlementId",
                table: "NonCashSettlementReversal",
                column: "SaleNonCashSettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturn_PurchaseId",
                table: "PurchaseReturn",
                column: "PurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnItem_PurchaseItemId",
                table: "PurchaseReturnItem",
                column: "PurchaseItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnItem_PurchaseReturnId",
                table: "PurchaseReturnItem",
                column: "PurchaseReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnItem_StockUnitId",
                table: "PurchaseReturnItem",
                column: "StockUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleNonCashSettlement_DeviceAcquisitionId",
                table: "SaleNonCashSettlement",
                column: "DeviceAcquisitionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleNonCashSettlement_SaleId",
                table: "SaleNonCashSettlement",
                column: "SaleId");

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerEntry_DeviceAcquisition_DeviceAcquisitionId",
                table: "LedgerEntry",
                column: "DeviceAcquisitionId",
                principalTable: "DeviceAcquisition",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerEntry_PurchaseReturn_PurchaseReturnId",
                table: "LedgerEntry",
                column: "PurchaseReturnId",
                principalTable: "PurchaseReturn",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LedgerEntry_DeviceAcquisition_DeviceAcquisitionId",
                table: "LedgerEntry");

            migrationBuilder.DropForeignKey(
                name: "FK_LedgerEntry_PurchaseReturn_PurchaseReturnId",
                table: "LedgerEntry");

            migrationBuilder.DropTable(
                name: "NonCashSettlementReversal");

            migrationBuilder.DropTable(
                name: "PurchaseReturnItem");

            migrationBuilder.DropTable(
                name: "SaleNonCashSettlement");

            migrationBuilder.DropTable(
                name: "PurchaseReturn");

            migrationBuilder.DropIndex(
                name: "IX_LedgerEntry_DeviceAcquisitionId",
                table: "LedgerEntry");

            migrationBuilder.DropIndex(
                name: "IX_LedgerEntry_PurchaseReturnId",
                table: "LedgerEntry");

            migrationBuilder.DropColumn(
                name: "ReturnedQuantity",
                table: "SaleCostAllocation");

            migrationBuilder.DropColumn(
                name: "BranchIds",
                table: "OperationReceipt");

            migrationBuilder.DropColumn(
                name: "Permissions",
                table: "OperationReceipt");

            migrationBuilder.DropColumn(
                name: "DeviceAcquisitionId",
                table: "LedgerEntry");

            migrationBuilder.DropColumn(
                name: "PurchaseReturnId",
                table: "LedgerEntry");
        }
    }
}
