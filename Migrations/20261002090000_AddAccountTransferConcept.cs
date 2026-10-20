using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PersonalFinance.Api.Data;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261002090000_AddAccountTransferConcept")]
    public partial class AddAccountTransferConcept : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TransferConceptId",
                table: "LedgerAccounts",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccounts_TransferConceptId",
                table: "LedgerAccounts",
                column: "TransferConceptId");

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerAccounts_Concepts_TransferConceptId",
                table: "LedgerAccounts",
                column: "TransferConceptId",
                principalTable: "Concepts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LedgerAccounts_Concepts_TransferConceptId",
                table: "LedgerAccounts");

            migrationBuilder.DropIndex(
                name: "IX_LedgerAccounts_TransferConceptId",
                table: "LedgerAccounts");

            migrationBuilder.DropColumn(
                name: "TransferConceptId",
                table: "LedgerAccounts");
        }
    }
}
