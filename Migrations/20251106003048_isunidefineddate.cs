using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class isunidefineddate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsIndefinite",
                table: "Incomes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "LoanId",
                table: "Incomes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsIndefinite",
                table: "Expenses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Incomes_LoanId",
                table: "Incomes",
                column: "LoanId");

            migrationBuilder.AddForeignKey(
                name: "FK_Incomes_Loans_LoanId",
                table: "Incomes",
                column: "LoanId",
                principalTable: "Loans",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Incomes_Loans_LoanId",
                table: "Incomes");

            migrationBuilder.DropIndex(
                name: "IX_Incomes_LoanId",
                table: "Incomes");

            migrationBuilder.DropColumn(
                name: "IsIndefinite",
                table: "Incomes");

            migrationBuilder.DropColumn(
                name: "LoanId",
                table: "Incomes");

            migrationBuilder.DropColumn(
                name: "IsIndefinite",
                table: "Expenses");
        }
    }
}
