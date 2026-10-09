using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShopLicensing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessLicense",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessLicense", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessLicense_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON "BusinessLicense" FOR EACH ROW EXECUTE FUNCTION invora_reject_history_change();
                DO $$ BEGIN IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'invora_runtime') THEN
                    GRANT SELECT, INSERT ON "BusinessLicense" TO invora_runtime;
                END IF; END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessLicense_BusinessId",
                table: "BusinessLicense",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessLicense_CreatedAtUtc",
                table: "BusinessLicense",
                column: "CreatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessLicense");
        }
    }
}
