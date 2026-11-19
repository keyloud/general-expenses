using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeneralExpenses.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersAndReceiptOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "OwnerUserId",
                table: "receipts",
                type: "bigint",
                nullable: false,
                defaultValue: -1L);

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "users",
                columns: new[] { "Id", "Name", "Username" },
                values: new object[] { -1L, "Локальный разработчик", null });

            migrationBuilder.CreateIndex(
                name: "IX_receipts_OwnerUserId",
                table: "receipts",
                column: "OwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_receipts_users_OwnerUserId",
                table: "receipts",
                column: "OwnerUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_receipts_users_OwnerUserId",
                table: "receipts");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropIndex(
                name: "IX_receipts_OwnerUserId",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "receipts");
        }
    }
}
