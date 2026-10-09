using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeviceDetailCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceCorrection",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StockUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: false),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceCorrection", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceCorrection_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceCorrection_StockUnit_StockUnitId",
                        column: x => x.StockUnitId,
                        principalTable: "StockUnit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCorrection_BranchId",
                table: "DeviceCorrection",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCorrection_StockUnitId_CreatedAtUtc",
                table: "DeviceCorrection",
                columns: new[] { "StockUnitId", "CreatedAtUtc" });

            migrationBuilder.Sql("""
                CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "DeviceCorrection" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
                DO $$ BEGIN IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'invora_runtime') THEN
                    GRANT SELECT, INSERT ON "DeviceCorrection" TO invora_runtime;
                END IF; END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceCorrection");
        }
    }
}
