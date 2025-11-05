using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class LoanIdExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExpenseId",
                table: "LoanPayments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LoanId",
                table: "Expenses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoanPayments_ExpenseId",
                table: "LoanPayments",
                column: "ExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_LoanId",
                table: "Expenses",
                column: "LoanId");

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Loans_LoanId",
                table: "Expenses",
                column: "LoanId",
                principalTable: "Loans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LoanPayments_Expenses_ExpenseId",
                table: "LoanPayments",
                column: "ExpenseId",
                principalTable: "Expenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Loans_LoanId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_LoanPayments_Expenses_ExpenseId",
                table: "LoanPayments");

            migrationBuilder.DropIndex(
                name: "IX_LoanPayments_ExpenseId",
                table: "LoanPayments");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_LoanId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "ExpenseId",
                table: "LoanPayments");

            migrationBuilder.DropColumn(
                name: "LoanId",
                table: "Expenses");
        }
    }
}
