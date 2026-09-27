using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PersonalFinance.Api.Data;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260927152000_AddPasanacoCompletion")]
    public partial class AddPasanacoCompletion : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "Pasanacos",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCompleted",
                table: "Pasanacos",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CompletedAt", table: "Pasanacos");
            migrationBuilder.DropColumn(name: "IsCompleted", table: "Pasanacos");
        }
    }
}
