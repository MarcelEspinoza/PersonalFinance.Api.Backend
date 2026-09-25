using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class CreateMissingBudgetsAndCommitmentsTables : Migration
    {
        /// <summary>
        /// Budgets y FinancialCommitments llevaban en el modelo (DbContext) desde
        /// hace tiempo pero ninguna migración llegó a crearlas realmente en la
        /// base de datos de producción (ver comentario en AddLedgerDomain). Se
        /// confirmó mediante SHOW TABLES contra la BD de producción que ninguna
        /// de las dos existe, así que esta migración las crea desde cero.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Budgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    MonthlyLimit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    StartMonth = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndMonth = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Budgets_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FinancialCommitments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpectedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Tolerance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    StartMonth = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndMonth = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    BankId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialCommitments", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_CategoryId",
                table: "Budgets",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_UserId_CategoryId",
                table: "Budgets",
                columns: new[] { "UserId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialCommitments_UserId_StartMonth",
                table: "FinancialCommitments",
                columns: new[] { "UserId", "StartMonth" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Budgets");
            migrationBuilder.DropTable(name: "FinancialCommitments");
        }
    }
}
