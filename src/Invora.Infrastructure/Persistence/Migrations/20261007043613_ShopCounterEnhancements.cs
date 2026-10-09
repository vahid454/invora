using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShopCounterEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AlternatePhone",
                table: "Party",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AadhaarLastFour",
                table: "DeviceInspection",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SellerName",
                table: "DeviceInspection",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "PaymentFollowUp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    PromiseDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NextContactDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentFollowUp", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentFollowUp_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentFollowUp_Party_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Party",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentFollowUp_BranchId_CustomerId_CreatedAtUtc",
                table: "PaymentFollowUp",
                columns: new[] { "BranchId", "CustomerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentFollowUp_CustomerId",
                table: "PaymentFollowUp",
                column: "CustomerId");
            migrationBuilder.Sql("""
                CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "PaymentFollowUp" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
                ALTER TABLE "DeviceInspection" ADD CONSTRAINT "CK_DeviceInspection_MaskedAadhaar" CHECK ("AadhaarLastFour" = '' OR "AadhaarLastFour" ~ '^[0-9]{4}$');
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'invora_runtime') THEN
                        GRANT SELECT, INSERT ON "PaymentFollowUp" TO invora_runtime;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"DeviceInspection\" DROP CONSTRAINT \"CK_DeviceInspection_MaskedAadhaar\";");
            migrationBuilder.DropTable(
                name: "PaymentFollowUp");

            migrationBuilder.DropColumn(
                name: "AlternatePhone",
                table: "Party");

            migrationBuilder.DropColumn(
                name: "AadhaarLastFour",
                table: "DeviceInspection");

            migrationBuilder.DropColumn(
                name: "SellerName",
                table: "DeviceInspection");
        }
    }
}
