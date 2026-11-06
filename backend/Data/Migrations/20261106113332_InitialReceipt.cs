using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeneralExpenses.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "receipts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    InstanceId = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "items",
                columns: table => new
                {
                    ReceiptId = table.Column<string>(type: "text", nullable: false),
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_items", x => new { x.ReceiptId, x.Id });
                    table.CheckConstraint("ck_item_amount", "\"Amount\" BETWEEN 1 AND 100000000");
                    table.ForeignKey(
                        name: "FK_items_receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "participants",
                columns: table => new
                {
                    ReceiptId = table.Column<string>(type: "text", nullable: false),
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Initial = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_participants", x => new { x.ReceiptId, x.Id });
                    table.ForeignKey(
                        name: "FK_participants_receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "assignments",
                columns: table => new
                {
                    ReceiptId = table.Column<string>(type: "text", nullable: false),
                    ItemId = table.Column<string>(type: "text", nullable: false),
                    ParticipantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assignments", x => new { x.ReceiptId, x.ItemId, x.ParticipantId });
                    table.ForeignKey(
                        name: "FK_assignments_items_ReceiptId_ItemId",
                        columns: x => new { x.ReceiptId, x.ItemId },
                        principalTable: "items",
                        principalColumns: new[] { "ReceiptId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_assignments_participants_ReceiptId_ParticipantId",
                        columns: x => new { x.ReceiptId, x.ParticipantId },
                        principalTable: "participants",
                        principalColumns: new[] { "ReceiptId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_assignments_ReceiptId_ParticipantId",
                table: "assignments",
                columns: new[] { "ReceiptId", "ParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_items_ReceiptId",
                table: "items",
                column: "ReceiptId",
                unique: true,
                filter: "\"IsDeleted\" = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignments");

            migrationBuilder.DropTable(
                name: "items");

            migrationBuilder.DropTable(
                name: "participants");

            migrationBuilder.DropTable(
                name: "receipts");
        }
    }
}
