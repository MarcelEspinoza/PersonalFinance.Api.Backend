using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services;

namespace PersonalFinance.Api.Tests;

/// <summary>
/// Tareas de arranque: los pasanacos sin propietario solo se asignan cuando hay
/// un único administrador, y se cuentan las cuentas pendientes de aprobación.
/// </summary>
public sealed class StartupSecurityTasksTests : LedgerTestBase
{
    public StartupSecurityTasksTests()
    {
        foreach (var role in new[] { "Admin", "User", "Invited" })
            Db.Roles.Add(new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = role, NormalizedName = role.ToUpperInvariant() });
        Db.SaveChanges();
    }

    [Fact]
    public async Task Los_pasanacos_sin_propietario_pasan_al_unico_admin_y_repetirlo_no_cambia_nada()
    {
        var admin = SeedUser("admin@test.local", "Admin");
        SeedUser("user@test.local", "User");
        var owned = SeedPasanaco(UserId);
        SeedPasanaco(null);
        SeedPasanaco(null);

        Assert.Equal(2, await StartupSecurityTasks.BackfillPasanacoOwnersAsync(Db, NullLogger.Instance));
        Assert.Equal(0, await StartupSecurityTasks.BackfillPasanacoOwnersAsync(Db, NullLogger.Instance));

        Db.ChangeTracker.Clear();
        var owners = await Db.Pasanacos.ToDictionaryAsync(p => p.Id, p => p.UserId);
        Assert.Equal(UserId, owners[owned.Id]);
        Assert.Equal(2, owners.Values.Count(owner => owner == admin.Id));
    }

    [Fact]
    public async Task Sin_un_unico_admin_no_se_asigna_ningun_pasanaco()
    {
        SeedUser("admin1@test.local", "Admin");
        SeedUser("admin2@test.local", "Admin");
        SeedPasanaco(null);

        Assert.Equal(0, await StartupSecurityTasks.BackfillPasanacoOwnersAsync(Db, NullLogger.Instance));

        Db.ChangeTracker.Clear();
        Assert.Null((await Db.Pasanacos.SingleAsync()).UserId);
    }

    [Fact]
    public async Task Cuenta_como_pendientes_los_usuarios_sin_rol_Admin_ni_User()
    {
        SeedUser("admin@test.local", "Admin");
        SeedUser("user@test.local", "User");
        SeedUser("invited@test.local", "Invited");
        SeedUser("sinrol@test.local");

        Assert.Equal(2, await StartupSecurityTasks.CountUsersPendingApprovalAsync(Db));
    }

    private ApplicationUser SeedUser(string email, params string[] roles)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant()
        };
        Db.Set<ApplicationUser>().Add(user);
        foreach (var role in roles)
            Db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = Db.Roles.Single(r => r.Name == role).Id });
        Db.SaveChanges();
        return user;
    }

    private Pasanaco SeedPasanaco(Guid? userId)
    {
        var pasanaco = new Pasanaco
        {
            UserId = userId,
            Name = "Pasanaco",
            MonthlyAmount = 100m,
            TotalParticipants = 5,
            CurrentRound = 1,
            StartMonth = 1,
            StartYear = 2026
        };
        Db.Pasanacos.Add(pasanaco);
        Db.SaveChanges();
        return pasanaco;
    }
}
