using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class NewFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PasanacoId",
                table: "Loans",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasanacoId",
                table: "Incomes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasanacoId",
                table: "Expenses",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Loans_PasanacoId",
                table: "Loans",
                column: "PasanacoId");

            migrationBuilder.CreateIndex(
                name: "IX_Incomes_PasanacoId",
                table: "Incomes",
                column: "PasanacoId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_PasanacoId",
                table: "Expenses",
                column: "PasanacoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Pasanacos_PasanacoId",
                table: "Expenses",
                column: "PasanacoId",
                principalTable: "Pasanacos",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Incomes_Pasanacos_PasanacoId",
                table: "Incomes",
                column: "PasanacoId",
                principalTable: "Pasanacos",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Loans_Pasanacos_PasanacoId",
                table: "Loans",
                column: "PasanacoId",
                principalTable: "Pasanacos",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Pasanacos_PasanacoId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Incomes_Pasanacos_PasanacoId",
                table: "Incomes");

            migrationBuilder.DropForeignKey(
                name: "FK_Loans_Pasanacos_PasanacoId",
                table: "Loans");

            migrationBuilder.DropIndex(
                name: "IX_Loans_PasanacoId",
                table: "Loans");

            migrationBuilder.DropIndex(
                name: "IX_Incomes_PasanacoId",
                table: "Incomes");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_PasanacoId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "PasanacoId",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "PasanacoId",
                table: "Incomes");

            migrationBuilder.DropColumn(
                name: "PasanacoId",
                table: "Expenses");
        }
    }
}
