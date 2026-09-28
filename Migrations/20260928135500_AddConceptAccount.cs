using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PersonalFinance.Api.Data;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260928135500_AddConceptAccount")]
    public partial class AddConceptAccount : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AccountId",
                table: "Concepts",
                type: "char(36)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Concepts_AccountId",
                table: "Concepts",
                column: "AccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_Concepts_LedgerAccounts_AccountId",
                table: "Concepts",
                column: "AccountId",
                principalTable: "LedgerAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Concepts_LedgerAccounts_AccountId",
                table: "Concepts");

            migrationBuilder.DropIndex(
                name: "IX_Concepts_AccountId",
                table: "Concepts");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "Concepts");
        }
    }
}
