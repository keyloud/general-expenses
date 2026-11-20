using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeneralExpenses.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InviteExpiresAt",
                table: "participants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InviteHash",
                table: "participants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UserId",
                table: "participants",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_participants_InviteHash",
                table: "participants",
                column: "InviteHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_participants_ReceiptId_UserId",
                table: "participants",
                columns: new[] { "ReceiptId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_participants_UserId",
                table: "participants",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_participants_users_UserId",
                table: "participants",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_participants_users_UserId",
                table: "participants");

            migrationBuilder.DropIndex(
                name: "IX_participants_InviteHash",
                table: "participants");

            migrationBuilder.DropIndex(
                name: "IX_participants_ReceiptId_UserId",
                table: "participants");

            migrationBuilder.DropIndex(
                name: "IX_participants_UserId",
                table: "participants");

            migrationBuilder.DropColumn(
                name: "InviteExpiresAt",
                table: "participants");

            migrationBuilder.DropColumn(
                name: "InviteHash",
                table: "participants");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "participants");
        }
    }
}
