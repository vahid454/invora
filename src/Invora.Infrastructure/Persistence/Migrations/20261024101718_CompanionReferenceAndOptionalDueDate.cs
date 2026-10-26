using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Invora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompanionReferenceAndOptionalDueDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ExistingDue_Amount",
                table: "ExistingDue");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "DueDate",
                table: "ExistingDue",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AddColumn<string>(
                name: "CompanionName",
                table: "ExistingDue",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CompanionPhone",
                table: "ExistingDue",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ExistingDue_Amount",
                table: "ExistingDue",
                sql: "\"Amount\" > 0 AND (\"DueDate\" IS NULL OR \"DueDate\" >= \"BusinessDate\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ExistingDue_Companion",
                table: "ExistingDue",
                sql: "(\"CompanionName\" = '' AND \"CompanionPhone\" = '') OR (\"CompanionName\" <> '' AND \"CompanionPhone\" <> '')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"ExistingDue\" WHERE \"DueDate\" IS NULL) THEN RAISE EXCEPTION 'Cannot roll back optional due dates while undated entries exist.'; END IF; END $$;");
            migrationBuilder.DropCheckConstraint(
                name: "CK_ExistingDue_Amount",
                table: "ExistingDue");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ExistingDue_Companion",
                table: "ExistingDue");

            migrationBuilder.DropColumn(
                name: "CompanionName",
                table: "ExistingDue");

            migrationBuilder.DropColumn(
                name: "CompanionPhone",
                table: "ExistingDue");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "DueDate",
                table: "ExistingDue",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ExistingDue_Amount",
                table: "ExistingDue",
                sql: "\"Amount\" > 0 AND \"DueDate\" >= \"BusinessDate\"");
        }
    }
}
