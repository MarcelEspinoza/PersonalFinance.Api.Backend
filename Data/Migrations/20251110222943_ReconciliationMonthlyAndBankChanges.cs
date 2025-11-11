using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReconciliationMonthlyAndBankChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Institution",
                table: "Banks",
                newName: "Entity");

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "Banks",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Color",
                table: "Banks");

            migrationBuilder.RenameColumn(
                name: "Entity",
                table: "Banks",
                newName: "Institution");
        }
    }
}
