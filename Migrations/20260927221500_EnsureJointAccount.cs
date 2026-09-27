using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PersonalFinance.Api.Data;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260927221500_EnsureJointAccount")]
    public partial class EnsureJointAccount : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE `LedgerAccounts` AS account
                INNER JOIN `ImportBatches` AS importBatch
                    ON importBatch.`AccountId` = account.`Id`
                    AND importBatch.`Source` = 0
                    AND importBatch.`FileName` LIKE '%fad8bc.csv'
                SET account.`Name` = 'Conjunta',
                    account.`Entity` = COALESCE(NULLIF(account.`Entity`, ''), 'Revolut'),
                    account.`OpeningBalance` = 14.26,
                    account.`OpeningDate` = '2026-10-01',
                    account.`IsActive` = 1;
                """);

            migrationBuilder.Sql("""
                INSERT INTO `Banks`
                    (`Id`, `UserId`, `Name`, `Entity`, `AccountNumber`, `Currency`, `CreatedAt`, `Color`)
                SELECT account.`Id`, account.`UserId`, account.`Name`, account.`Entity`,
                       account.`AccountNumber`, account.`Currency`, account.`CreatedAt`, account.`Color`
                FROM `LedgerAccounts` AS account
                INNER JOIN `ImportBatches` AS importBatch
                    ON importBatch.`AccountId` = account.`Id`
                    AND importBatch.`Source` = 0
                    AND importBatch.`FileName` LIKE '%fad8bc.csv'
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM `Banks` AS bank
                    WHERE bank.`Id` = account.`Id`
                );
                """);

            migrationBuilder.Sql("""
                UPDATE `Banks` AS bank
                INNER JOIN `LedgerAccounts` AS account
                    ON account.`Id` = bank.`Id`
                INNER JOIN `ImportBatches` AS importBatch
                    ON importBatch.`AccountId` = account.`Id`
                    AND importBatch.`Source` = 0
                    AND importBatch.`FileName` LIKE '%fad8bc.csv'
                SET bank.`Name` = account.`Name`,
                    bank.`Entity` = account.`Entity`,
                    bank.`AccountNumber` = account.`AccountNumber`,
                    bank.`Currency` = account.`Currency`,
                    bank.`Color` = account.`Color`;
                """);

            migrationBuilder.Sql("""
                UPDATE `MonthlyPeriods` AS period
                INNER JOIN `LedgerAccounts` AS account
                    ON account.`UserId` = period.`UserId`
                    AND account.`Name` = 'Conjunta'
                    AND account.`OpeningDate` = '2026-10-01'
                SET period.`CarryOverAmount` = 32.68,
                    period.`ClosingBalance` = NULL,
                    period.`ClosedAt` = NULL,
                    period.`Status` = 0
                WHERE period.`Year` = 2026
                  AND period.`Month` = 10;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The imported account and its historical data must remain intact.
        }
    }
}
