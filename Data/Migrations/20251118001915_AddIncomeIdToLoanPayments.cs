using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIncomeIdToLoanPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IncomeId",
                table: "LoanPayments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoanPayments_IncomeId",
                table: "LoanPayments",
                column: "IncomeId");

            migrationBuilder.AddForeignKey(
                name: "FK_LoanPayments_Incomes_IncomeId",
                table: "LoanPayments",
                column: "IncomeId",
                principalTable: "Incomes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LoanPayments_Incomes_IncomeId",
                table: "LoanPayments");

            migrationBuilder.DropIndex(
                name: "IX_LoanPayments_IncomeId",
                table: "LoanPayments");

            migrationBuilder.DropColumn(
                name: "IncomeId",
                table: "LoanPayments");
        }
    }
}
