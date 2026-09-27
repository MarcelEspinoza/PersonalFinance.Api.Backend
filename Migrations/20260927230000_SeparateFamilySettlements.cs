using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PersonalFinance.Api.Data;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260927230000_SeparateFamilySettlements")]
    public partial class SeparateFamilySettlements : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE `Concepts`
                SET `Name` = 'Reembolsos de Vanessa',
                    `Nature` = 1,
                    `DefaultMonthlyBudget` = NULL
                WHERE `Name` = 'Liquidaciones familiares';
                """);

            AddConcept(
                migrationBuilder,
                "Ingresos",
                "Liquidaciones recibidas de mamá",
                kind: 0,
                sortOrder: 60);
            AddConcept(
                migrationBuilder,
                "Finanzas y compromisos",
                "Liquidaciones pagadas a mamá",
                kind: 1,
                sortOrder: 100);

            UpdateMapping(
                migrationBuilder,
                "TRANSFERENCIA DE VANESSA ISABEL MARQUEZ DE CABALLERO",
                "Reembolsos de Vanessa");
            UpdateMapping(
                migrationBuilder,
                "TRANSFERENCIA DE JENNY MABEL ESPINOZA SEJAS",
                "Liquidaciones recibidas de mamá",
                "Revolut 6931");
            UpdateMapping(
                migrationBuilder,
                "TRANSFERENCIA A JENNY MABEL ESPINOZA SEJAS",
                "Liquidaciones pagadas a mamá",
                "Revolut 6931");

            ReclassifyPersonalRows(
                migrationBuilder,
                "TRANSFERENCIA DE JENNY MABEL ESPINOZA SEJAS",
                "Liquidaciones recibidas de mamá");
            ReclassifyPersonalRows(
                migrationBuilder,
                "TRANSFERENCIA A JENNY MABEL ESPINOZA SEJAS",
                "Liquidaciones pagadas a mamá");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Historical classifications are intentionally preserved.
        }

        private static void AddConcept(
            MigrationBuilder migrationBuilder,
            string groupName,
            string conceptName,
            int kind,
            int sortOrder)
        {
            migrationBuilder.Sql($$"""
                INSERT INTO `Concepts`
                    (`Id`, `UserId`, `GroupId`, `Name`, `Kind`, `Nature`,
                     `DefaultMonthlyBudget`, `SortOrder`, `IsActive`, `CreatedAt`)
                SELECT UUID(), conceptGroup.`UserId`, conceptGroup.`Id`, '{{conceptName}}',
                       {{kind}}, 1, NULL, {{sortOrder}}, 1, UTC_TIMESTAMP(6)
                FROM `ConceptGroups` AS conceptGroup
                WHERE conceptGroup.`Name` = '{{groupName}}'
                  AND conceptGroup.`IsActive` = 1
                  AND NOT EXISTS (
                      SELECT 1
                      FROM `Concepts` AS concept
                      WHERE concept.`UserId` = conceptGroup.`UserId`
                        AND concept.`Name` = '{{conceptName}}'
                        AND concept.`IsActive` = 1
                  );
                """);
        }

        private static void UpdateMapping(
            MigrationBuilder migrationBuilder,
            string pattern,
            string conceptName,
            string? accountName = null)
        {
            var accountJoin = accountName is null
                ? ""
                : $"""
                  INNER JOIN `LedgerAccounts` AS account
                      ON account.`UserId` = concept.`UserId`
                      AND account.`Name` = '{accountName}'
                  """;
            var accountId = accountName is null ? "NULL" : "account.`Id`";
            var mappingScope = accountName is null
                ? "mapping.`AccountId` IS NULL"
                : $"""
                  mapping.`AccountId` IN (
                      SELECT account.`Id`
                      FROM `LedgerAccounts` AS account
                      WHERE account.`UserId` = mapping.`UserId`
                        AND account.`Name` = '{accountName}'
                  )
                  """;

            migrationBuilder.Sql($$"""
                DELETE mapping
                FROM `ConceptMappings` AS mapping
                WHERE mapping.`Pattern` = '{{pattern}}'
                  AND {{mappingScope}}
                  AND mapping.`UserId` IN (
                      SELECT concept.`UserId`
                      FROM `Concepts` AS concept
                      WHERE concept.`Name` = '{{conceptName}}'
                        AND concept.`IsActive` = 1
                  );

                INSERT INTO `ConceptMappings`
                    (`Id`, `UserId`, `AccountId`, `Pattern`, `ConceptId`, `Priority`,
                     `TimesApplied`, `IsActive`, `CreatedAt`, `LastUsedAt`)
                SELECT UUID(), concept.`UserId`, {{accountId}}, '{{pattern}}', concept.`Id`,
                       100, 0, 1, UTC_TIMESTAMP(6), NULL
                FROM `Concepts` AS concept
                {{accountJoin}}
                WHERE concept.`Name` = '{{conceptName}}'
                  AND concept.`IsActive` = 1;
                """);
        }

        private static void ReclassifyPersonalRows(
            MigrationBuilder migrationBuilder,
            string pattern,
            string conceptName)
        {
            migrationBuilder.Sql($$"""
                UPDATE `ImportRows` AS importRow
                INNER JOIN `ImportBatches` AS importBatch
                    ON importBatch.`Id` = importRow.`BatchId`
                INNER JOIN `LedgerAccounts` AS account
                    ON account.`Id` = importBatch.`AccountId`
                    AND account.`Name` = 'Revolut 6931'
                INNER JOIN `Concepts` AS concept
                    ON concept.`UserId` = importRow.`UserId`
                    AND concept.`Name` = '{{conceptName}}'
                    AND concept.`IsActive` = 1
                SET importRow.`SuggestedConceptId` = concept.`Id`,
                    importRow.`ConfirmedConceptId` = CASE
                        WHEN importRow.`ConfirmedConceptId` IS NULL THEN NULL
                        ELSE concept.`Id`
                    END
                WHERE importRow.`NormalizedDescription` = '{{pattern}}';

                UPDATE `LedgerEntries` AS entry
                INNER JOIN `ImportRows` AS importRow
                    ON importRow.`Id` = entry.`ImportRowId`
                INNER JOIN `ImportBatches` AS importBatch
                    ON importBatch.`Id` = importRow.`BatchId`
                INNER JOIN `LedgerAccounts` AS account
                    ON account.`Id` = importBatch.`AccountId`
                    AND account.`Name` = 'Revolut 6931'
                INNER JOIN `Concepts` AS concept
                    ON concept.`UserId` = entry.`UserId`
                    AND concept.`Name` = '{{conceptName}}'
                    AND concept.`IsActive` = 1
                SET entry.`ConceptId` = concept.`Id`
                WHERE importRow.`NormalizedDescription` = '{{pattern}}';
                """);
        }
    }
}
