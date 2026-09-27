using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PersonalFinance.Api.Data;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260927171000_RenameRevolutAccount")]
    public partial class RenameRevolutAccount : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE `LedgerAccounts` AS account
                INNER JOIN `ImportBatches` AS importBatch
                    ON importBatch.`AccountId` = account.`Id`
                    AND importBatch.`Source` = 0
                LEFT JOIN `LedgerAccounts` AS existingAccount
                    ON existingAccount.`UserId` = account.`UserId`
                    AND existingAccount.`Name` = 'Revolut 6931'
                    AND existingAccount.`Id` <> account.`Id`
                SET account.`Name` = 'Revolut 6931'
                WHERE account.`Name` = '6931'
                  AND existingAccount.`Id` IS NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE `Banks` AS bank
                INNER JOIN `LedgerAccounts` AS account
                    ON account.`Id` = bank.`Id`
                SET bank.`Name` = account.`Name`
                WHERE account.`Name` = 'Revolut 6931'
                  AND EXISTS (
                      SELECT 1
                      FROM `ImportBatches` AS importBatch
                      WHERE importBatch.`AccountId` = account.`Id`
                        AND importBatch.`Source` = 0
                  );
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A later user rename must not be overwritten during a rollback.
        }
    }
}
