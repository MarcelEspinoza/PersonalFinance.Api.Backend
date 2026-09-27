using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PersonalFinance.Api.Data;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260927222500_SeedIncomeAndHouseholdPlanning")]
    public partial class SeedIncomeAndHouseholdPlanning : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE `LedgerAccounts` AS account
                INNER JOIN `ImportBatches` AS importBatch
                    ON importBatch.`AccountId` = account.`Id`
                    AND importBatch.`Source` = 0
                    AND (
                        importBatch.`FileName` LIKE '%fad8bc%'
                        OR importBatch.`FileName` LIKE '%2026-03-01_2026-09-25%'
                    )
                SET account.`Name` = 'Conjunta',
                    account.`Entity` = 'Revolut',
                    account.`OpeningBalance` = 14.26,
                    account.`OpeningDate` = '2026-10-01',
                    account.`IsActive` = 1;
                """);

            migrationBuilder.Sql("""
                INSERT INTO `Banks`
                    (`Id`, `UserId`, `Name`, `Entity`, `AccountNumber`, `Currency`, `CreatedAt`, `Color`)
                SELECT account.`Id`, account.`UserId`, 'Conjunta', 'Revolut',
                       account.`AccountNumber`, account.`Currency`, account.`CreatedAt`, account.`Color`
                FROM `LedgerAccounts` AS account
                WHERE account.`Name` = 'Conjunta'
                  AND account.`OpeningDate` = '2026-10-01'
                  AND NOT EXISTS (
                      SELECT 1 FROM `Banks` AS bank WHERE bank.`Id` = account.`Id`
                  );
                """);

            migrationBuilder.Sql("""
                UPDATE `Banks` AS bank
                INNER JOIN `LedgerAccounts` AS account ON account.`Id` = bank.`Id`
                SET bank.`Name` = 'Conjunta',
                    bank.`Entity` = 'Revolut',
                    bank.`AccountNumber` = account.`AccountNumber`,
                    bank.`Currency` = account.`Currency`,
                    bank.`Color` = account.`Color`
                WHERE account.`Name` = 'Conjunta'
                  AND account.`OpeningDate` = '2026-10-01';
                """);

            AddMonthlyRule(migrationBuilder, "Alquiler", "Conjunta", 1, 737.97m, 7);
            AddMonthlyRule(migrationBuilder, "Agua", "Conjunta", 1, 74m, 7);
            AddMonthlyRule(migrationBuilder, "Electricidad", "Conjunta", 1, 92m, 19);
            AddMonthlyRule(migrationBuilder, "Gas", "Conjunta", 1, 85m, 4);
            AddMonthlyRule(migrationBuilder, "Internet y telefonía", "Conjunta", 1, 70.60m, 3);
            AddMonthlyRule(migrationBuilder, "Seguridad del hogar", "Conjunta", 1, 60.11m, 7);
            AddMonthlyRule(migrationBuilder, "Seguro del hogar", "Conjunta", 1, 38.70m, 4);
            AddMonthlyRule(migrationBuilder, "Nómina", "Revolut 6931", 0, 2360m, 29);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // User-editable planning is intentionally preserved.
        }

        private static void AddMonthlyRule(
            MigrationBuilder migrationBuilder,
            string conceptName,
            string accountName,
            int direction,
            decimal amount,
            int dayOfMonth)
        {
            migrationBuilder.Sql($$"""
                INSERT INTO `RecurringRules`
                    (`Id`, `UserId`, `ConceptId`, `AccountId`, `Description`,
                     `Direction`, `Frequency`, `DayOfMonth`, `ForecastAmount`,
                     `StartDate`, `EndDate`, `IsActive`, `CreatedAt`)
                SELECT UUID(), concept.`UserId`, concept.`Id`, account.`Id`, '{{conceptName}}',
                       {{direction}}, 0, {{dayOfMonth}},
                       {{amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
                       '2026-10-01', NULL, 1, UTC_TIMESTAMP(6)
                FROM `Concepts` AS concept
                INNER JOIN `LedgerAccounts` AS account
                    ON account.`UserId` = concept.`UserId`
                    AND account.`Name` = '{{accountName}}'
                    AND account.`OpeningDate` = '2026-10-01'
                WHERE concept.`Name` = '{{conceptName}}'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM `RecurringRules` AS rule
                      WHERE rule.`UserId` = concept.`UserId`
                        AND rule.`ConceptId` = concept.`Id`
                        AND rule.`IsActive` = 1
                  );
                """);
        }
    }
}
