using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PersonalFinance.Api.Data;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260927232000_CorrectCaserAsDentalInsurance")]
    public partial class CorrectCaserAsDentalInsurance : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE `Concepts` AS concept
                INNER JOIN `ConceptGroups` AS conceptGroup
                    ON conceptGroup.`UserId` = concept.`UserId`
                    AND conceptGroup.`Name` = 'Salud y cuidado personal'
                    AND conceptGroup.`IsActive` = 1
                SET concept.`Name` = 'Seguro dental',
                    concept.`GroupId` = conceptGroup.`Id`,
                    concept.`Nature` = 0,
                    concept.`DefaultMonthlyBudget` = NULL,
                    concept.`SortOrder` = 20,
                    concept.`IsActive` = 1
                WHERE concept.`Name` = 'Seguro del hogar';

                UPDATE `RecurringRules` AS rule
                INNER JOIN `Concepts` AS concept
                    ON concept.`Id` = rule.`ConceptId`
                    AND concept.`UserId` = rule.`UserId`
                    AND concept.`Name` = 'Seguro dental'
                INNER JOIN `LedgerAccounts` AS account
                    ON account.`UserId` = rule.`UserId`
                    AND account.`Name` = 'Conjunta'
                    AND account.`IsActive` = 1
                SET rule.`AccountId` = account.`Id`,
                    rule.`Description` = 'Seguro dental',
                    rule.`Direction` = 1,
                    rule.`Frequency` = 0,
                    rule.`DayOfMonth` = 1,
                    rule.`ForecastAmount` = 21.58,
                    rule.`StartDate` = '2026-10-01',
                    rule.`EndDate` = NULL,
                    rule.`IsActive` = 1
                WHERE rule.`IsActive` = 1;

                INSERT INTO `RecurringRules`
                    (`Id`, `UserId`, `ConceptId`, `AccountId`, `Description`,
                     `Direction`, `Frequency`, `DayOfMonth`, `ForecastAmount`,
                     `StartDate`, `EndDate`, `IsActive`, `CreatedAt`)
                SELECT UUID(), concept.`UserId`, concept.`Id`, account.`Id`, 'Seguro dental',
                       1, 0, 1, 21.58, '2026-10-01', NULL, 1, UTC_TIMESTAMP(6)
                FROM `Concepts` AS concept
                INNER JOIN `LedgerAccounts` AS account
                    ON account.`UserId` = concept.`UserId`
                    AND account.`Name` = 'Conjunta'
                    AND account.`IsActive` = 1
                WHERE concept.`Name` = 'Seguro dental'
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
                    AND concept.`Name` = 'Seguro dental'
                SET entry.`AccountId` = rule.`AccountId`,
                    entry.`Description` = 'Seguro dental',
                    entry.`ForecastAmount` = 21.58,
                    entry.`DueDate` = STR_TO_DATE(
                        CONCAT(
                            YEAR(entry.`DueDate`), '-',
                            LPAD(MONTH(entry.`DueDate`), 2, '0'), '-01'
                        ),
                        '%Y-%m-%d'
                    ),
                    entry.`UpdatedAt` = UTC_TIMESTAMP(6)
                WHERE entry.`DueDate` >= '2026-10-01'
                  AND entry.`Status` <> 2;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The corrected user classification and planning are preserved.
        }
    }
}
