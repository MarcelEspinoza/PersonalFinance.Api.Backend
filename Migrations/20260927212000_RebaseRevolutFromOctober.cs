using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PersonalFinance.Api.Data;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260927212000_RebaseRevolutFromOctober")]
    public partial class RebaseRevolutFromOctober : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE `LedgerAccounts` AS account
                INNER JOIN `ImportBatches` AS importBatch
                    ON importBatch.`AccountId` = account.`Id`
                    AND importBatch.`Source` = 0
                SET account.`OpeningBalance` = 18.42,
                    account.`OpeningDate` = '2026-10-01'
                WHERE account.`Name` = 'Revolut 6931';
                """);

            migrationBuilder.Sql("""
                UPDATE `LedgerAccounts` AS account
                INNER JOIN `ImportBatches` AS importBatch
                    ON importBatch.`AccountId` = account.`Id`
                    AND importBatch.`Source` = 0
                SET account.`OpeningBalance` = 14.26,
                    account.`OpeningDate` = '2026-10-01'
                WHERE account.`Name` = 'Conjunta';
                """);

            migrationBuilder.Sql("""
                INSERT INTO `Banks`
                    (`Id`, `UserId`, `Name`, `Entity`, `AccountNumber`, `Currency`, `CreatedAt`, `Color`)
                SELECT account.`Id`, account.`UserId`, account.`Name`,
                       COALESCE(account.`Entity`, 'Revolut'), account.`AccountNumber`,
                       account.`Currency`, account.`CreatedAt`, account.`Color`
                FROM `LedgerAccounts` AS account
                WHERE account.`Name` = 'Conjunta'
                  AND account.`OpeningDate` = '2026-10-01'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM `Banks` AS bank
                      WHERE bank.`Id` = account.`Id`
                  );
                """);

            migrationBuilder.Sql("""
                UPDATE `MonthlyPeriods` AS period
                INNER JOIN `LedgerAccounts` AS account
                    ON account.`UserId` = period.`UserId`
                    AND account.`Name` = 'Revolut 6931'
                    AND account.`OpeningDate` = '2026-10-01'
                SET period.`CarryOverAmount` = 32.68,
                    period.`ClosingBalance` = NULL,
                    period.`ClosedAt` = NULL,
                    period.`Status` = 0
                WHERE period.`Year` = 2026
                  AND period.`Month` = 10;
                """);

            migrationBuilder.Sql("""
                UPDATE `Concepts` AS concept
                INNER JOIN `LedgerAccounts` AS account
                    ON account.`UserId` = concept.`UserId`
                    AND account.`Name` = 'Revolut 6931'
                    AND account.`OpeningDate` = '2026-10-01'
                SET concept.`DefaultMonthlyBudget` = CASE concept.`Name`
                    WHEN 'Supermercado y alimentación del hogar' THEN 320.00
                    WHEN 'Restaurantes y cafeterías' THEN 120.00
                    WHEN 'Transporte diario' THEN 100.00
                    WHEN 'Ropa y calzado' THEN 70.00
                    WHEN 'Mascotas' THEN 80.00
                    WHEN 'Belleza y peluquería' THEN 110.00
                    WHEN 'Ocio y entretenimiento' THEN 120.00
                    WHEN 'Viajes y alojamiento' THEN 240.00
                    WHEN 'Formación' THEN 20.00
                    ELSE concept.`DefaultMonthlyBudget`
                END
                WHERE concept.`Name` IN (
                    'Supermercado y alimentación del hogar',
                    'Restaurantes y cafeterías',
                    'Transporte diario',
                    'Ropa y calzado',
                    'Mascotas',
                    'Belleza y peluquería',
                    'Ocio y entretenimiento',
                    'Viajes y alojamiento',
                    'Formación'
                );
                """);

            AddMonthlyExpenseRule(migrationBuilder, "Alquiler", "Conjunta", 737.97m, 7);
            AddMonthlyExpenseRule(migrationBuilder, "Agua", "Conjunta", 74m, 7);
            AddMonthlyExpenseRule(migrationBuilder, "Electricidad", "Conjunta", 92m, 19);
            AddMonthlyExpenseRule(migrationBuilder, "Gas", "Conjunta", 85m, 4);
            AddMonthlyExpenseRule(migrationBuilder, "Internet y telefonía", "Conjunta", 70.60m, 3);
            AddMonthlyExpenseRule(migrationBuilder, "Seguridad del hogar", "Conjunta", 60.11m, 7);
            AddMonthlyExpenseRule(migrationBuilder, "Seguro del hogar", "Conjunta", 38.70m, 4);
            AddMonthlyExpenseRule(migrationBuilder, "Netflix", "Revolut 6931", 8.99m, 3);
            AddMonthlyExpenseRule(migrationBuilder, "Software y servicios digitales", "Revolut 6931", 20m, 7);

            migrationBuilder.Sql("""
                INSERT INTO `ConceptMappings`
                    (`Id`, `UserId`, `AccountId`, `Pattern`, `ConceptId`, `Priority`,
                     `TimesApplied`, `IsActive`, `CreatedAt`, `LastUsedAt`)
                SELECT UUID(), concept.`UserId`, account.`Id`,
                       'HECTAREA SERVICIOS INMOBILIARIOS', concept.`Id`, 90,
                       0, 1, UTC_TIMESTAMP(6), NULL
                FROM `Concepts` AS concept
                INNER JOIN `LedgerAccounts` AS account
                    ON account.`UserId` = concept.`UserId`
                    AND account.`Name` = 'Conjunta'
                    AND account.`OpeningDate` = '2026-10-01'
                WHERE concept.`Name` = 'Alquiler'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM `ConceptMappings` AS mapping
                      WHERE mapping.`UserId` = concept.`UserId`
                        AND mapping.`AccountId` = account.`Id`
                        AND mapping.`Pattern` = 'HECTAREA SERVICIOS INMOBILIARIOS'
                  );
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous baseline is intentionally preserved as historical data.
        }

        private static void AddMonthlyExpenseRule(
            MigrationBuilder migrationBuilder,
            string conceptName,
            string accountName,
            decimal amount,
            int dayOfMonth)
        {
            migrationBuilder.Sql($$"""
                INSERT INTO `RecurringRules`
                    (`Id`, `UserId`, `ConceptId`, `AccountId`, `Description`,
                     `Direction`, `Frequency`, `DayOfMonth`, `ForecastAmount`,
                     `StartDate`, `EndDate`, `IsActive`, `CreatedAt`)
                SELECT UUID(), concept.`UserId`, concept.`Id`, account.`Id`, '{{conceptName}}',
                       1, 0, {{dayOfMonth}}, {{amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
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
