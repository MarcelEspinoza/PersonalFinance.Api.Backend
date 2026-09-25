using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class UnifyAccountsWithBanks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountNumber",
                table: "LedgerAccounts",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "LedgerAccounts",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Entity",
                table: "LedgerAccounts",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            // Copia cada Banco existente a LedgerAccounts (mismo Id) para que a
            // partir de ahora exista una sola lista de cuentas. Se omiten los
            // bancos que ya tengan una cuenta con ese Id (por si la migración
            // se ejecuta más de una vez).
            migrationBuilder.Sql(@"
                INSERT INTO LedgerAccounts
                    (Id, UserId, Name, Type, Currency, OpeningBalance, OpeningDate, IsActive, CreatedAt, Entity, AccountNumber, Color)
                SELECT
                    b.Id, b.UserId, b.Name, 0, COALESCE(NULLIF(b.Currency, ''), 'EUR'), 0, DATE(b.CreatedAt), 1, b.CreatedAt, b.Entity, b.AccountNumber, b.Color
                FROM Banks b
                WHERE NOT EXISTS (SELECT 1 FROM LedgerAccounts a WHERE a.Id = b.Id);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountNumber",
                table: "LedgerAccounts");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "LedgerAccounts");

            migrationBuilder.DropColumn(
                name: "Entity",
                table: "LedgerAccounts");
        }
    }
}
