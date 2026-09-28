using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PersonalFinance.Api.Data;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260928135500_AddConceptAccount")]
    public partial class AddConceptAccount : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                SET @account_column_exists = (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'Concepts'
                      AND COLUMN_NAME = 'AccountId'
                );
                SET @account_column_sql = IF(
                    @account_column_exists = 0,
                    'ALTER TABLE `Concepts` ADD COLUMN `AccountId` char(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL',
                    'ALTER TABLE `Concepts` MODIFY COLUMN `AccountId` char(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL'
                );
                PREPARE account_column_stmt FROM @account_column_sql;
                EXECUTE account_column_stmt;
                DEALLOCATE PREPARE account_column_stmt;

                SET @account_index_exists = (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.STATISTICS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'Concepts'
                      AND INDEX_NAME = 'IX_Concepts_AccountId'
                );
                SET @account_index_sql = IF(
                    @account_index_exists = 0,
                    'CREATE INDEX `IX_Concepts_AccountId` ON `Concepts` (`AccountId`)',
                    'SELECT 1'
                );
                PREPARE account_index_stmt FROM @account_index_sql;
                EXECUTE account_index_stmt;
                DEALLOCATE PREPARE account_index_stmt;

                SET @account_fk_exists = (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
                    WHERE CONSTRAINT_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'Concepts'
                      AND CONSTRAINT_NAME = 'FK_Concepts_LedgerAccounts_AccountId'
                );
                SET @account_fk_sql = IF(
                    @account_fk_exists = 0,
                    'ALTER TABLE `Concepts` ADD CONSTRAINT `FK_Concepts_LedgerAccounts_AccountId` FOREIGN KEY (`AccountId`) REFERENCES `LedgerAccounts` (`Id`) ON DELETE SET NULL',
                    'SELECT 1'
                );
                PREPARE account_fk_stmt FROM @account_fk_sql;
                EXECUTE account_fk_stmt;
                DEALLOCATE PREPARE account_fk_stmt;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Concepts_LedgerAccounts_AccountId",
                table: "Concepts");

            migrationBuilder.DropIndex(
                name: "IX_Concepts_AccountId",
                table: "Concepts");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "Concepts");
        }
    }
}
