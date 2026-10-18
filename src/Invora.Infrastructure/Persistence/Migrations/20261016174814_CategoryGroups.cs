using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CategoryGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Group",
                table: "ProductCategory",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "Electronics");
            migrationBuilder.Sql("""
                UPDATE "ProductCategory" SET "Group" = 'Mobile' WHERE "RequiresImei" = true;
                UPDATE "ProductCategory" SET "Group" = 'Accessories' WHERE "NormalizedName" IN ('ACCESSORY', 'ACCESSORIES') AND "RequiresImei" = false;
                ALTER TABLE "ProductCategory" ADD CONSTRAINT "CK_ProductCategory_Group" CHECK ("Group" IN ('Mobile', 'Electronics', 'Accessories'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"ProductCategory\" DROP CONSTRAINT \"CK_ProductCategory_Group\";");
            migrationBuilder.DropColumn(
                name: "Group",
                table: "ProductCategory");
        }
    }
}
