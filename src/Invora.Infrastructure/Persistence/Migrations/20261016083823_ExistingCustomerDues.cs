using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExistingCustomerDues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Allocation_Target",
                table: "PaymentAllocation");

            migrationBuilder.AddColumn<Guid>(
                name: "ExistingDueId",
                table: "PaymentFollowUp",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SaleId",
                table: "PaymentFollowUp",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExistingDueId",
                table: "PaymentAllocation",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExistingDueId",
                table: "LedgerEntry",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExistingDue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    FinanceReference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExistingDue", x => x.Id);
                    table.CheckConstraint("CK_ExistingDue_Amount", "\"Amount\" > 0 AND \"DueDate\" >= \"BusinessDate\"");
                    table.ForeignKey(
                        name: "FK_ExistingDue_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExistingDue_Party_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Party",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentFollowUp_ExistingDueId",
                table: "PaymentFollowUp",
                column: "ExistingDueId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentFollowUp_SaleId",
                table: "PaymentFollowUp",
                column: "SaleId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FollowUp_Target",
                table: "PaymentFollowUp",
                sql: "num_nonnulls(\"SaleId\", \"ExistingDueId\") <= 1");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocation_ExistingDueId",
                table: "PaymentAllocation",
                column: "ExistingDueId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Allocation_Target",
                table: "PaymentAllocation",
                sql: "num_nonnulls(\"SaleId\", \"PurchaseId\", \"ExistingDueId\") = 1 AND \"Amount\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntry_ExistingDueId",
                table: "LedgerEntry",
                column: "ExistingDueId");

            migrationBuilder.CreateIndex(
                name: "IX_ExistingDue_BranchId_CustomerId_DueDate",
                table: "ExistingDue",
                columns: new[] { "BranchId", "CustomerId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ExistingDue_BranchId_Reference",
                table: "ExistingDue",
                columns: new[] { "BranchId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExistingDue_CustomerId",
                table: "ExistingDue",
                column: "CustomerId");

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerEntry_ExistingDue_ExistingDueId",
                table: "LedgerEntry",
                column: "ExistingDueId",
                principalTable: "ExistingDue",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentAllocation_ExistingDue_ExistingDueId",
                table: "PaymentAllocation",
                column: "ExistingDueId",
                principalTable: "ExistingDue",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentFollowUp_ExistingDue_ExistingDueId",
                table: "PaymentFollowUp",
                column: "ExistingDueId",
                principalTable: "ExistingDue",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentFollowUp_Sale_SaleId",
                table: "PaymentFollowUp",
                column: "SaleId",
                principalTable: "Sale",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "ExistingDue" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
                DO $$ BEGIN IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'invora_runtime') THEN
                    GRANT SELECT, INSERT ON "ExistingDue" TO invora_runtime;
                END IF; END $$;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LedgerEntry_ExistingDue_ExistingDueId",
                table: "LedgerEntry");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentAllocation_ExistingDue_ExistingDueId",
                table: "PaymentAllocation");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentFollowUp_ExistingDue_ExistingDueId",
                table: "PaymentFollowUp");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentFollowUp_Sale_SaleId",
                table: "PaymentFollowUp");

            migrationBuilder.DropTable(
                name: "ExistingDue");

            migrationBuilder.DropIndex(
                name: "IX_PaymentFollowUp_ExistingDueId",
                table: "PaymentFollowUp");

            migrationBuilder.DropIndex(
                name: "IX_PaymentFollowUp_SaleId",
                table: "PaymentFollowUp");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FollowUp_Target",
                table: "PaymentFollowUp");

            migrationBuilder.DropIndex(
                name: "IX_PaymentAllocation_ExistingDueId",
                table: "PaymentAllocation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Allocation_Target",
                table: "PaymentAllocation");

            migrationBuilder.DropIndex(
                name: "IX_LedgerEntry_ExistingDueId",
                table: "LedgerEntry");

            migrationBuilder.DropColumn(
                name: "ExistingDueId",
                table: "PaymentFollowUp");

            migrationBuilder.DropColumn(
                name: "SaleId",
                table: "PaymentFollowUp");

            migrationBuilder.DropColumn(
                name: "ExistingDueId",
                table: "PaymentAllocation");

            migrationBuilder.DropColumn(
                name: "ExistingDueId",
                table: "LedgerEntry");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Allocation_Target",
                table: "PaymentAllocation",
                sql: "(\"SaleId\" IS NULL) <> (\"PurchaseId\" IS NULL) AND \"Amount\" > 0");
        }
    }
}
