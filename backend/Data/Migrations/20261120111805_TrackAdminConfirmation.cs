using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeneralExpenses.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrackAdminConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ConfirmedByAdminId",
                table: "participants",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmedByAdminId",
                table: "participants");
        }
    }
}
