using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddImportRowPlannedAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportRowPlannedAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ImportRowId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PlannedEntryId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportRowPlannedAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportRowPlannedAllocations_ImportRows_ImportRowId",
                        column: x => x.ImportRowId,
                        principalTable: "ImportRows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImportRowPlannedAllocations_LedgerEntries_PlannedEntryId",
                        column: x => x.PlannedEntryId,
                        principalTable: "LedgerEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRowPlannedAllocations_ImportRowId_PlannedEntryId",
                table: "ImportRowPlannedAllocations",
                columns: new[] { "ImportRowId", "PlannedEntryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportRowPlannedAllocations_PlannedEntryId",
                table: "ImportRowPlannedAllocations",
                column: "PlannedEntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportRowPlannedAllocations");
        }
    }
}
