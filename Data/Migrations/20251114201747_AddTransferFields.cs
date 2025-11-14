using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTransfer",
                table: "Incomes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "TransferCounterpartyBankId",
                table: "Incomes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransferId",
                table: "Incomes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransferReference",
                table: "Incomes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTransfer",
                table: "Expenses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "TransferCounterpartyBankId",
                table: "Expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransferId",
                table: "Expenses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransferReference",
                table: "Expenses",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsTransfer",
                table: "Incomes");

            migrationBuilder.DropColumn(
                name: "TransferCounterpartyBankId",
                table: "Incomes");

            migrationBuilder.DropColumn(
                name: "TransferId",
                table: "Incomes");

            migrationBuilder.DropColumn(
                name: "TransferReference",
                table: "Incomes");

            migrationBuilder.DropColumn(
                name: "IsTransfer",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "TransferCounterpartyBankId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "TransferId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "TransferReference",
                table: "Expenses");
        }
    }
}
