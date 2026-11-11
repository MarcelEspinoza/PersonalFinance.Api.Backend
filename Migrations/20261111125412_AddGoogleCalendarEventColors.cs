using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleCalendarEventColors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PasanacoColorId",
                table: "GoogleCalendarConnections",
                type: "int",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "ZeusColorId",
                table: "GoogleCalendarConnections",
                type: "int",
                nullable: false,
                defaultValue: 3);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PasanacoColorId",
                table: "GoogleCalendarConnections");

            migrationBuilder.DropColumn(
                name: "ZeusColorId",
                table: "GoogleCalendarConnections");
        }
    }
}
