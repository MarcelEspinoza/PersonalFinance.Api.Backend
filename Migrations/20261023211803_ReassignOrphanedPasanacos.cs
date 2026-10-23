using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReassignOrphanedPasanacos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One-time fix: pasanacos whose owner was deleted or lost every role move to the
            // single Admin. Nothing changes unless there is exactly one Admin.
            migrationBuilder.Sql(@"
UPDATE Pasanacos
SET UserId = (
    SELECT MIN(ur.UserId)
    FROM AspNetUserRoles ur
    INNER JOIN AspNetRoles r ON r.Id = ur.RoleId
    WHERE r.NormalizedName = 'ADMIN')
WHERE UserId IS NOT NULL
  AND UserId NOT IN (
    SELECT ur2.UserId
    FROM AspNetUserRoles ur2
    INNER JOIN AspNetRoles r2 ON r2.Id = ur2.RoleId
    WHERE r2.NormalizedName IN ('ADMIN', 'USER'))
  AND (
    SELECT COUNT(*)
    FROM AspNetUserRoles ur3
    INNER JOIN AspNetRoles r3 ON r3.Id = ur3.RoleId
    WHERE r3.NormalizedName = 'ADMIN') = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-only fix: the previous owners no longer exist.
        }
    }
}