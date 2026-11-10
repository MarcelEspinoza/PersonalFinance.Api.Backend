using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddImportRowObligationLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ReminderDueDate",
                table: "ImportRows",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReminderId",
                table: "ImportRows",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "SettlementId",
                table: "ImportRows",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRows_ReminderId",
                table: "ImportRows",
                column: "ReminderId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRows_SettlementId",
                table: "ImportRows",
                column: "SettlementId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ImportRows_ReminderId",
                table: "ImportRows");

            migrationBuilder.DropIndex(
                name: "IX_ImportRows_SettlementId",
                table: "ImportRows");

            migrationBuilder.DropColumn(
                name: "ReminderDueDate",
                table: "ImportRows");

            migrationBuilder.DropColumn(
                name: "ReminderId",
                table: "ImportRows");

            migrationBuilder.DropColumn(
                name: "SettlementId",
                table: "ImportRows");
        }
    }
}
