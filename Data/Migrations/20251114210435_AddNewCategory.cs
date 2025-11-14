using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNewCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "CreatedAt", "Description", "IsActive", "IsSystem", "Name", "UpdatedAt", "UserId" },
                values: new object[] { 400, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Categoría de sistema para traspasos internos", true, true, "Transferencia", null, new Guid("00000000-0000-0000-0000-000000000000") });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 400);
        }
    }
}
