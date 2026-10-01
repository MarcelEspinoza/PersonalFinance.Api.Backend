using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PersonalFinance.Api.Data;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261001090000_LinkSettlementLoan")]
    public partial class LinkSettlementLoan : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LinkedLoanId",
                table: "Settlements",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<decimal>(
                name: "LinkedLoanBalanceSnapshot",
                table: "Settlements",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_LinkedLoanId",
                table: "Settlements",
                column: "LinkedLoanId");

            migrationBuilder.AddForeignKey(
                name: "FK_Settlements_Loans_LinkedLoanId",
                table: "Settlements",
                column: "LinkedLoanId",
                principalTable: "Loans",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Settlements_Loans_LinkedLoanId",
                table: "Settlements");

            migrationBuilder.DropIndex(
                name: "IX_Settlements_LinkedLoanId",
                table: "Settlements");

            migrationBuilder.DropColumn(
                name: "LinkedLoanId",
                table: "Settlements");

            migrationBuilder.DropColumn(
                name: "LinkedLoanBalanceSnapshot",
                table: "Settlements");
        }
    }
}
