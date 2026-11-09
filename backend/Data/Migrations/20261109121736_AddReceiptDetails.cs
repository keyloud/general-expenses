using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeneralExpenses.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "receipts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "receipts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Ужин с друзьями");

            migrationBuilder.CreateIndex(
                name: "IX_receipts_CreatedAt",
                table: "receipts",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_receipts_CreatedAt",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "receipts");
        }
    }
}
