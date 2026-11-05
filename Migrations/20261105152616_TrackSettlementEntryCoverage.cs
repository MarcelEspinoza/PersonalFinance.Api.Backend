using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class TrackSettlementEntryCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllocatesPendingEntry",
                table: "SettlementLines",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "SettlementAllocatedAmount",
                table: "LedgerEntries",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SettlementSettledAmount",
                table: "LedgerEntries",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllocatesPendingEntry",
                table: "SettlementLines");

            migrationBuilder.DropColumn(
                name: "SettlementAllocatedAmount",
                table: "LedgerEntries");

            migrationBuilder.DropColumn(
                name: "SettlementSettledAmount",
                table: "LedgerEntries");
        }
    }
}
