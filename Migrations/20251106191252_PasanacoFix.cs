using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class PasanacoFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Participants_PasanacoId",
                table: "Participants");

            migrationBuilder.AddColumn<Guid>(
                name: "PaidByLoanId",
                table: "PasanacoPayments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Participants_PasanacoId_AssignedNumber",
                table: "Participants",
                columns: new[] { "PasanacoId", "AssignedNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Participants_PasanacoId_AssignedNumber",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "PaidByLoanId",
                table: "PasanacoPayments");

            migrationBuilder.CreateIndex(
                name: "IX_Participants_PasanacoId",
                table: "Participants",
                column: "PasanacoId");
        }
    }
}
