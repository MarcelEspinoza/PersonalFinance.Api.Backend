using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PersonalFinance.Api.Data;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260927231000_ForceHouseholdPlanningValues")]
    public partial class ForceHouseholdPlanningValues : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            UpsertMonthlyRule(migrationBuilder, "Alquiler", 737.97m, 7);
            UpsertMonthlyRule(migrationBuilder, "Agua", 74m, 7);
            UpsertMonthlyRule(migrationBuilder, "Electricidad", 92m, 19);
            UpsertMonthlyRule(migrationBuilder, "Gas", 85m, 4);
            UpsertMonthlyRule(migrationBuilder, "Internet y telefonía", 70.60m, 3);
            UpsertMonthlyRule(migrationBuilder, "Seguridad del hogar", 60.11m, 7);
            UpsertMonthlyRule(migrationBuilder, "Seguro del hogar", 38.70m, 4);
            UpsertMonthlyRule(migrationBuilder, "Seguro médico", 180.39m, 1);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // User-editable planning is intentionally preserved.
        }

        private static void UpsertMonthlyRule(
            MigrationBuilder migrationBuilder,
            string conceptName,
            decimal amount,
            int dayOfMonth)
        {
            var invariantAmount = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);

            migrationBuilder.Sql($$"""
                UPDATE `RecurringRules` AS rule
                INNER JOIN `Concepts` AS concept
                    ON concept.`Id` = rule.`ConceptId`
                    AND concept.`UserId` = rule.`UserId`
                    AND concept.`Name` = '{{conceptName}}'
                INNER JOIN `LedgerAccounts` AS account
                    ON account.`UserId` = rule.`UserId`
                    AND account.`Name` = 'Conjunta'
                    AND account.`IsActive` = 1
                SET rule.`AccountId` = account.`Id`,
                    rule.`Description` = '{{conceptName}}',
                    rule.`Direction` = 1,
                    rule.`Frequency` = 0,
                    rule.`DayOfMonth` = {{dayOfMonth}},
                    rule.`ForecastAmount` = {{invariantAmount}},
                    rule.`StartDate` = '2026-10-01',
                    rule.`EndDate` = NULL,
                    rule.`IsActive` = 1
                WHERE rule.`IsActive` = 1;

                INSERT INTO `RecurringRules`
                    (`Id`, `UserId`, `ConceptId`, `AccountId`, `Description`,
                     `Direction`, `Frequency`, `DayOfMonth`, `ForecastAmount`,
                     `StartDate`, `EndDate`, `IsActive`, `CreatedAt`)
                SELECT UUID(), concept.`UserId`, concept.`Id`, account.`Id`, '{{conceptName}}',
                       1, 0, {{dayOfMonth}}, {{invariantAmount}},
                       '2026-10-01', NULL, 1, UTC_TIMESTAMP(6)
                FROM `Concepts` AS concept
                INNER JOIN `LedgerAccounts` AS account
                    ON account.`UserId` = concept.`UserId`
                    AND account.`Name` = 'Conjunta'
                    AND account.`IsActive` = 1
                WHERE concept.`Name` = '{{conceptName}}'
                  AND concept.`IsActive` = 1
                  AND NOT EXISTS (
                      SELECT 1
                      FROM `RecurringRules` AS rule
                      WHERE rule.`UserId` = concept.`UserId`
                        AND rule.`ConceptId` = concept.`Id`
                        AND rule.`IsActive` = 1
                  );

                UPDATE `LedgerEntries` AS entry
                INNER JOIN `RecurringRules` AS rule
                    ON rule.`Id` = entry.`RecurringRuleId`
                    AND rule.`IsActive` = 1
                INNER JOIN `Concepts` AS concept
                    ON concept.`Id` = rule.`ConceptId`
                    AND concept.`Name` = '{{conceptName}}'
                SET entry.`AccountId` = rule.`AccountId`,
                    entry.`Description` = '{{conceptName}}',
                    entry.`ForecastAmount` = {{invariantAmount}},
                    entry.`DueDate` = STR_TO_DATE(
                        CONCAT(
                            YEAR(entry.`DueDate`), '-',
                            LPAD(MONTH(entry.`DueDate`), 2, '0'), '-',
                            LPAD(LEAST({{dayOfMonth}}, DAY(LAST_DAY(entry.`DueDate`))), 2, '0')
                        ),
                        '%Y-%m-%d'
                    ),
                    entry.`UpdatedAt` = UTC_TIMESTAMP(6)
                WHERE entry.`DueDate` >= '2026-10-01'
                  AND entry.`Status` <> 2;
                """);
        }
    }
}
