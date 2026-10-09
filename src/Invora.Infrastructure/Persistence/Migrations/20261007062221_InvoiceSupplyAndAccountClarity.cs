using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceSupplyAndAccountClarity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Reference",
                table: "Sale",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Reference",
                table: "Sale");
        }
    }
}
