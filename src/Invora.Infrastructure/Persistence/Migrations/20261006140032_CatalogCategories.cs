using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductCategory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RequiresImei = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCategory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductCategory_NormalizedName",
                table: "ProductCategory",
                column: "NormalizedName",
                unique: true);
            migrationBuilder.Sql("""
                INSERT INTO "ProductCategory" ("Id", "Name", "NormalizedName", "RequiresImei", "CreatedAtUtc", "Version")
                VALUES (gen_random_uuid(), 'Mobile', 'MOBILE', true, now(), 0),
                       (gen_random_uuid(), 'Electronics', 'ELECTRONICS', false, now(), 0),
                       (gen_random_uuid(), 'Accessory', 'ACCESSORY', false, now(), 0);
                INSERT INTO "ProductCategory" ("Id", "Name", "NormalizedName", "RequiresImei", "CreatedAtUtc", "Version")
                SELECT gen_random_uuid(), min(trim("Category")), upper(trim("Category")),
                       upper(trim("Category")) IN ('MOBILE', 'SMARTPHONE', 'MOBILE PHONE', 'PHONE'), now(), 0
                FROM "ProductModel" WHERE trim("Category") <> '' GROUP BY upper(trim("Category"))
                ON CONFLICT ("NormalizedName") DO NOTHING;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'invora_runtime') THEN
                        GRANT SELECT, INSERT ON "ProductCategory" TO invora_runtime;
                    END IF;
                END $$;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductCategory");
        }
    }
}
