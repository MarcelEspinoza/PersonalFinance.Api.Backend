using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddImportBatchBankBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BankBalance",
                table: "ImportBatches",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BankBalanceDate",
                table: "ImportBatches",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExcludedRows",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BankBalance",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "BankBalanceDate",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ExcludedRows",
                table: "ImportBatches");
        }
    }
}
