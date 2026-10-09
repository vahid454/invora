using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeviceVariantsAndBrands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Brand",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Brand", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Brand_NormalizedName",
                table: "Brand",
                column: "NormalizedName",
                unique: true);
            // Add suggestions without changing existing products or posted invoices.
            migrationBuilder.Sql("""
                INSERT INTO "Brand" ("Id", "Name", "NormalizedName", "CreatedAtUtc", "Version")
                SELECT gen_random_uuid(), name, upper(name), now(), 0
                FROM unnest(ARRAY['Apple','Samsung','Realme','Oppo','Vivo','Redmi','Poco','Honor','Moto','Motorola','Tecno','Infinix','OnePlus','Nothing','Google','Nokia','Sony','Asus','Lenovo','HP','Dell','Acer','LG','Generic']) AS name
                ON CONFLICT ("NormalizedName") DO NOTHING;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'invora_runtime') THEN
                        GRANT SELECT, INSERT ON "Brand" TO invora_runtime;
                    END IF;
                END $$;
                INSERT INTO "Brand" ("Id", "Name", "NormalizedName", "CreatedAtUtc", "Version")
                SELECT gen_random_uuid(), min(trim("Brand")), upper(trim("Brand")), now(), 0
                FROM "ProductModel" WHERE trim("Brand") <> '' GROUP BY upper(trim("Brand"))
                ON CONFLICT ("NormalizedName") DO NOTHING;
                INSERT INTO "TaxRate" ("Id", "Name", "Rate", "CessRate", "IsActive", "CreatedAtUtc", "Version")
                SELECT gen_random_uuid(), 'GST ' || rate || '%', rate, 0, true, now(), 0
                FROM unnest(ARRAY[0,5,18,40]) AS rate
                WHERE NOT EXISTS (SELECT 1 FROM "TaxRate" t WHERE t."Rate" = rate AND t."CessRate" = 0 AND t."IsActive");
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Brand");
        }
    }
}
