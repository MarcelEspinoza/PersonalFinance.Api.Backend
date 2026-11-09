using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedImportExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsExpenseReimbursement",
                table: "Settlements",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "CounterpartyId",
                table: "ImportRowAllocations",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRowAllocations_CounterpartyId",
                table: "ImportRowAllocations",
                column: "CounterpartyId");

            migrationBuilder.AddForeignKey(
                name: "FK_ImportRowAllocations_Counterparties_CounterpartyId",
                table: "ImportRowAllocations",
                column: "CounterpartyId",
                principalTable: "Counterparties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ImportRowAllocations_Counterparties_CounterpartyId",
                table: "ImportRowAllocations");

            migrationBuilder.DropIndex(
                name: "IX_ImportRowAllocations_CounterpartyId",
                table: "ImportRowAllocations");

            migrationBuilder.DropColumn(
                name: "IsExpenseReimbursement",
                table: "Settlements");

            migrationBuilder.DropColumn(
                name: "CounterpartyId",
                table: "ImportRowAllocations");
        }
    }
}
