using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PersonalFinance.Api.Data;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260927143500_RemoveErroneousRevolutImport")]
    public partial class RemoveErroneousRevolutImport : Migration
    {
        private const string FileName = "account-statement_2026-03-01_2026-09-25_es-es_fad8bc";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                DELETE ledgerEntry
                FROM `LedgerEntries` AS ledgerEntry
                INNER JOIN `ImportRows` AS importRow
                    ON ledgerEntry.`ImportRowId` = importRow.`Id`
                INNER JOIN `ImportBatches` AS importBatch
                    ON importRow.`BatchId` = importBatch.`Id`
                WHERE importBatch.`CreatedAt` >= '2026-01-01 00:00:00'
                  AND importBatch.`CreatedAt` < '2027-01-01 00:00:00'
                  AND importBatch.`FileName` IN ('{FileName}', '{FileName}.csv');
                """);

            migrationBuilder.Sql($"""
                DELETE FROM `ImportBatches`
                WHERE `CreatedAt` >= '2026-01-01 00:00:00'
                  AND `CreatedAt` < '2027-01-01 00:00:00'
                  AND `FileName` IN ('{FileName}', '{FileName}.csv');
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The deleted import cannot be recreated without the original CSV.
        }
    }
}
