using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PersonalFinance.Api.Data;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260928180000_AddImportRowAllocations")]
    public partial class AddImportRowAllocations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportRowAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ImportRowId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ConceptId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportRowAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportRowAllocations_Concepts_ConceptId",
                        column: x => x.ConceptId,
                        principalTable: "Concepts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportRowAllocations_ImportRows_ImportRowId",
                        column: x => x.ImportRowId,
                        principalTable: "ImportRows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRowAllocations_ConceptId",
                table: "ImportRowAllocations",
                column: "ConceptId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRowAllocations_ImportRowId",
                table: "ImportRowAllocations",
                column: "ImportRowId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ImportRowAllocations");
        }
    }
}
