using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupplierInvoiceFinancialYear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Purchase_SupplierId",
                table: "Purchase");

            migrationBuilder.AddColumn<int>(
                name: "FinancialYear",
                table: "Purchase",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
ALTER TABLE "Purchase" DISABLE TRIGGER immutable_document;
UPDATE "Purchase" AS p SET "FinancialYear" = CASE WHEN EXTRACT(MONTH FROM p."BusinessDate") >= b."FinancialYearStartMonth" THEN EXTRACT(YEAR FROM p."BusinessDate")::int ELSE EXTRACT(YEAR FROM p."BusinessDate")::int - 1 END FROM "Businesses" b;
ALTER TABLE "Purchase" ENABLE TRIGGER immutable_document;
""");
            migrationBuilder.CreateIndex(
                name: "IX_Purchase_SupplierId_SupplierInvoice_FinancialYear",
                table: "Purchase",
                columns: new[] { "SupplierId", "SupplierInvoice", "FinancialYear" },
                unique: true,
                filter: "\"Status\" = 'Completed'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Purchase_SupplierId_SupplierInvoice_FinancialYear",
                table: "Purchase");

            migrationBuilder.DropColumn(
                name: "FinancialYear",
                table: "Purchase");

            migrationBuilder.CreateIndex(
                name: "IX_Purchase_SupplierId",
                table: "Purchase",
                column: "SupplierId");
        }
    }
}
