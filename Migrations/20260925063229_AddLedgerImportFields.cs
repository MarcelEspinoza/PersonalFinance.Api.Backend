using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerImportFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConceptMappings_UserId_Pattern",
                table: "ConceptMappings");

            migrationBuilder.AddColumn<bool>(
                name: "IsTransfer",
                table: "LedgerEntries",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "AccountId",
                table: "ConceptMappings",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_ConceptMappings_AccountId",
                table: "ConceptMappings",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ConceptMappings_UserId_AccountId_Pattern",
                table: "ConceptMappings",
                columns: new[] { "UserId", "AccountId", "Pattern" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ConceptMappings_LedgerAccounts_AccountId",
                table: "ConceptMappings",
                column: "AccountId",
                principalTable: "LedgerAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConceptMappings_LedgerAccounts_AccountId",
                table: "ConceptMappings");

            migrationBuilder.DropIndex(
                name: "IX_ConceptMappings_AccountId",
                table: "ConceptMappings");

            migrationBuilder.DropIndex(
                name: "IX_ConceptMappings_UserId_AccountId_Pattern",
                table: "ConceptMappings");

            migrationBuilder.DropColumn(
                name: "IsTransfer",
                table: "LedgerEntries");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "ConceptMappings");

            migrationBuilder.CreateIndex(
                name: "IX_ConceptMappings_UserId_Pattern",
                table: "ConceptMappings",
                columns: new[] { "UserId", "Pattern" },
                unique: true);
        }
    }
}
