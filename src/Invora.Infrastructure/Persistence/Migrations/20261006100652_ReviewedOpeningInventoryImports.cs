using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewedOpeningInventoryImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "ImportJob",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportJob_BranchId",
                table: "ImportJob",
                column: "BranchId");

            migrationBuilder.AddForeignKey(
                name: "FK_ImportJob_Branches_BranchId",
                table: "ImportJob",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ImportJob_Branches_BranchId",
                table: "ImportJob");

            migrationBuilder.DropIndex(
                name: "IX_ImportJob_BranchId",
                table: "ImportJob");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "ImportJob");
        }
    }
}
