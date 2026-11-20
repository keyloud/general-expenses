using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeneralExpenses.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveItemUndo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ранее удалённые строки хранились для отмены. Убираем их до снятия признака.
            migrationBuilder.Sql("""DELETE FROM items WHERE "IsDeleted" = TRUE""");
            migrationBuilder.DropIndex(
                name: "IX_items_ReceiptId",
                table: "items");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "items");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_items_ReceiptId",
                table: "items",
                column: "ReceiptId",
                unique: true,
                filter: "\"IsDeleted\" = TRUE");
        }
    }
}
