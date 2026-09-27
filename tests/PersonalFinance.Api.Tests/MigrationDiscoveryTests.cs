using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;

namespace PersonalFinance.Api.Tests;

public class MigrationDiscoveryTests
{
    [Fact]
    public void ErroneousImportCleanupMigration_IsDiscoverable()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(
                "Server=localhost;Database=test;User=root;Password=test;",
                new MySqlServerVersion(new Version(8, 0, 0)))
            .Options;

        using var context = new AppDbContext(options);

        Assert.Contains(
            "20260927143500_RemoveErroneousRevolutImport",
            context.Database.GetMigrations());
        Assert.Contains(
            "20260927152000_AddPasanacoCompletion",
            context.Database.GetMigrations());
        Assert.Contains(
            "20260927171000_RenameRevolutAccount",
            context.Database.GetMigrations());
        Assert.Contains(
            "20260927210000_AddExpensePlanningDefaults",
            context.Database.GetMigrations());
    }
}
