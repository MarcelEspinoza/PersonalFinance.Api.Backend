using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PersonalFinance.Api.Data;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260927224000_DetailSubscriptionsAndKnownExpenses")]
    public partial class DetailSubscriptionsAndKnownExpenses : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE `Concepts`
                SET `Name` = 'Mascotas (Zoey y Zeus)',
                    `Nature` = 1,
                    `DefaultMonthlyBudget` = 100.00
                WHERE `Name` = 'Mascotas';
                """);

            AddConcept(migrationBuilder, "Amazon Prime", 0, 40, null);
            AddConcept(migrationBuilder, "Apple", 0, 50, null);
            AddConcept(migrationBuilder, "Spliiit", 1, 60, null);
            AddIncomeConcept(migrationBuilder, "Liquidaciones familiares", 1, 50);

            migrationBuilder.Sql("""
                UPDATE `Concepts`
                SET `Nature` = 0,
                    `DefaultMonthlyBudget` = NULL
                WHERE `Name` IN ('Seguro médico', 'Transporte diario');
                """);

            AddMapping(migrationBuilder, "AMAZON PRIME", "Amazon Prime", 90);
            AddMapping(migrationBuilder, "APPLE", "Apple", 80);
            AddMapping(migrationBuilder, "SPLIIIT", "Spliiit", 80);
            AddMapping(migrationBuilder, "O2", "Internet y telefonía", 70);
            AddMapping(migrationBuilder, "PLENITUDE", "Gas", 70);
            AddMapping(
                migrationBuilder,
                "TRANSFERENCIA DE VANESSA ISABEL MARQUEZ DE CABALLERO",
                "Liquidaciones familiares",
                100);

            ReclassifyImportedRows(migrationBuilder, "AMAZON PRIME", "Amazon Prime");
            ReclassifyImportedRows(migrationBuilder, "APPLE", "Apple", exactMatch: true);
            ReclassifyImportedRows(migrationBuilder, "SPLIIIT", "Spliiit");
            ReclassifyImportedRows(
                migrationBuilder,
                "TRANSFERENCIA DE VANESSA ISABEL MARQUEZ DE CABALLERO",
                "Liquidaciones familiares",
                exactMatch: true);

            AddMonthlyRule(migrationBuilder, "Seguro médico", "Conjunta", 180.39m, 1);
            AddMonthlyRule(migrationBuilder, "Transporte diario", "Revolut 6931", 22.80m, 5);
            AddMonthlyRule(migrationBuilder, "Amazon Prime", "Revolut 6931", 4.99m, 7);
            AddMonthlyRule(migrationBuilder, "Apple", "Revolut 6931", 9.99m, 18);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Historical classifications and user-editable planning are preserved.
        }

        private static void AddConcept(
            MigrationBuilder migrationBuilder,
            string name,
            int nature,
            int sortOrder,
            decimal? defaultMonthlyBudget)
        {
            var budget = defaultMonthlyBudget?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "NULL";
            migrationBuilder.Sql($$"""
                INSERT INTO `Concepts`
                    (`Id`, `UserId`, `GroupId`, `Name`, `Kind`, `Nature`,
                     `DefaultMonthlyBudget`, `SortOrder`, `IsActive`, `CreatedAt`)
                SELECT UUID(), conceptGroup.`UserId`, conceptGroup.`Id`, '{{name}}',
                       1, {{nature}}, {{budget}}, {{sortOrder}}, 1, UTC_TIMESTAMP(6)
                FROM `ConceptGroups` AS conceptGroup
                WHERE conceptGroup.`Name` = 'Suscripciones y servicios digitales'
                  AND conceptGroup.`IsActive` = 1
                  AND NOT EXISTS (
                      SELECT 1
                      FROM `Concepts` AS concept
                      WHERE concept.`UserId` = conceptGroup.`UserId`
                        AND concept.`Name` = '{{name}}'
                        AND concept.`IsActive` = 1
                  );
                """);
        }

        private static void AddMapping(
            MigrationBuilder migrationBuilder,
            string pattern,
            string conceptName,
            int priority)
        {
            migrationBuilder.Sql($$"""
                INSERT INTO `ConceptMappings`
                    (`Id`, `UserId`, `AccountId`, `Pattern`, `ConceptId`, `Priority`,
                     `TimesApplied`, `IsActive`, `CreatedAt`, `LastUsedAt`)
                SELECT UUID(), concept.`UserId`, NULL, '{{pattern}}', concept.`Id`,
                       {{priority}}, 0, 1, UTC_TIMESTAMP(6), NULL
                FROM `Concepts` AS concept
                WHERE concept.`Name` = '{{conceptName}}'
                  AND concept.`IsActive` = 1
                  AND NOT EXISTS (
                      SELECT 1
                      FROM `ConceptMappings` AS mapping
                      WHERE mapping.`UserId` = concept.`UserId`
                        AND mapping.`Pattern` = '{{pattern}}'
                  );

                UPDATE `ConceptMappings` AS mapping
                INNER JOIN `Concepts` AS concept
                    ON concept.`UserId` = mapping.`UserId`
                    AND concept.`Name` = '{{conceptName}}'
                    AND concept.`IsActive` = 1
                SET mapping.`ConceptId` = concept.`Id`,
                    mapping.`Priority` = {{priority}},
                    mapping.`IsActive` = 1
                WHERE mapping.`Pattern` = '{{pattern}}';
                """);
        }

        private static void AddIncomeConcept(
            MigrationBuilder migrationBuilder,
            string name,
            int nature,
            int sortOrder)
        {
            migrationBuilder.Sql($$"""
                INSERT INTO `Concepts`
                    (`Id`, `UserId`, `GroupId`, `Name`, `Kind`, `Nature`,
                     `DefaultMonthlyBudget`, `SortOrder`, `IsActive`, `CreatedAt`)
                SELECT UUID(), conceptGroup.`UserId`, conceptGroup.`Id`, '{{name}}',
                       0, {{nature}}, NULL, {{sortOrder}}, 1, UTC_TIMESTAMP(6)
                FROM `ConceptGroups` AS conceptGroup
                WHERE conceptGroup.`Name` = 'Ingresos'
                  AND conceptGroup.`IsActive` = 1
                  AND NOT EXISTS (
                      SELECT 1
                      FROM `Concepts` AS concept
                      WHERE concept.`UserId` = conceptGroup.`UserId`
                        AND concept.`Name` = '{{name}}'
                        AND concept.`IsActive` = 1
                  );
                """);
        }

        private static void ReclassifyImportedRows(
            MigrationBuilder migrationBuilder,
            string pattern,
            string conceptName,
            bool exactMatch = false)
        {
            var comparison = exactMatch
                ? $"importRow.`NormalizedDescription` = '{pattern}'"
                : $"importRow.`NormalizedDescription` LIKE '%{pattern}%'";

            migrationBuilder.Sql($$"""
                UPDATE `ImportRows` AS importRow
                INNER JOIN `Concepts` AS concept
                    ON concept.`UserId` = importRow.`UserId`
                    AND concept.`Name` = '{{conceptName}}'
                    AND concept.`IsActive` = 1
                SET importRow.`SuggestedConceptId` = concept.`Id`,
                    importRow.`ConfirmedConceptId` = CASE
                        WHEN importRow.`ConfirmedConceptId` IS NULL THEN NULL
                        ELSE concept.`Id`
                    END
                WHERE {{comparison}};

                UPDATE `LedgerEntries` AS entry
                INNER JOIN `ImportRows` AS importRow
                    ON importRow.`Id` = entry.`ImportRowId`
                INNER JOIN `Concepts` AS concept
                    ON concept.`UserId` = entry.`UserId`
                    AND concept.`Name` = '{{conceptName}}'
                    AND concept.`IsActive` = 1
                SET entry.`ConceptId` = concept.`Id`
                WHERE {{comparison}};
                """);
        }

        private static void AddMonthlyRule(
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
                       1, 0, {{dayOfMonth}},
                       {{amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
                       '2026-10-01', NULL, 1, UTC_TIMESTAMP(6)
                FROM `Concepts` AS concept
                INNER JOIN `LedgerAccounts` AS account
                    ON account.`UserId` = concept.`UserId`
                    AND account.`Name` = '{{accountName}}'
                    AND account.`OpeningDate` = '2026-10-01'
                WHERE concept.`Name` = '{{conceptName}}'
                  AND concept.`IsActive` = 1
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
