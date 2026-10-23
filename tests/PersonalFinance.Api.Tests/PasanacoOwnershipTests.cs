using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Api.MappingProfiles;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Models;
using PersonalFinance.Api.Models.Dtos.Pasanaco;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services;

namespace PersonalFinance.Api.Tests;

/// <summary>
/// Un pasanaco solo existe para su propietario: para cualquier otro usuario se
/// comporta como inexistente, y los que no tienen propietario no los ve nadie.
/// </summary>
public sealed class PasanacoOwnershipTests : LedgerTestBase
{
    private readonly Guid _otherUserId = Guid.NewGuid();

    [Fact]
    public async Task Otro_usuario_no_puede_leer_modificar_completar_ni_borrar_un_pasanaco_ajeno()
    {
        var service = CreateService();
        var pasanaco = await service.CreateAsync(UserId, NewPasanaco("Familia"));

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(pasanaco.Id, _otherUserId));
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(pasanaco.Id, _otherUserId, new UpdatePasanacoDto
        {
            Name = "Cambiado",
            MonthlyAmount = 1m,
            TotalParticipants = 2,
            CurrentRound = 1,
            StartMonth = 1,
            StartYear = 2026
        }));
        await Assert.ThrowsAsync<NotFoundException>(() => service.CompleteAsync(pasanaco.Id, _otherUserId));
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(pasanaco.Id, _otherUserId));
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeletePasanacoCascadeAsync(pasanaco.Id, _otherUserId));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetParticipantsAsync(pasanaco.Id, _otherUserId));

        Db.ChangeTracker.Clear();
        var stored = await Db.Pasanacos.SingleAsync(p => p.Id == pasanaco.Id);
        Assert.Equal(UserId, stored.UserId);
        Assert.Equal("Familia", stored.Name);
        Assert.Equal(100m, stored.MonthlyAmount);
        Assert.False(stored.IsCompleted);
    }

    [Fact]
    public async Task El_propietario_si_puede_modificar_completar_y_borrar_su_pasanaco()
    {
        var service = CreateService();
        var pasanaco = await service.CreateAsync(UserId, NewPasanaco("Familia"));

        await service.UpdateAsync(pasanaco.Id, UserId, new UpdatePasanacoDto
        {
            Name = "Familia 2026",
            MonthlyAmount = 120m,
            TotalParticipants = 5,
            CurrentRound = 1,
            StartMonth = 1,
            StartYear = 2026
        });
        await service.CompleteAsync(pasanaco.Id, UserId);

        var updated = await service.GetByIdAsync(pasanaco.Id, UserId);
        Assert.Equal("Familia 2026", updated.Name);
        Assert.True(updated.IsCompleted);

        await service.DeleteAsync(pasanaco.Id, UserId);
        Assert.False(await Db.Pasanacos.AnyAsync());
    }

    [Fact]
    public async Task El_listado_solo_devuelve_los_pasanacos_propios()
    {
        var service = CreateService();
        var mine = await service.CreateAsync(UserId, NewPasanaco("Mío"));
        var theirs = await service.CreateAsync(_otherUserId, NewPasanaco("Ajeno"));
        var orphan = new Pasanaco
        {
            Name = "Sin propietario",
            MonthlyAmount = 10m,
            TotalParticipants = 2,
            CurrentRound = 1,
            StartMonth = 1,
            StartYear = 2026
        };
        Db.Pasanacos.Add(orphan);
        await Db.SaveChangesAsync();

        Assert.Equal(new[] { mine.Id }, (await service.GetAllAsync(UserId)).Select(p => p.Id));
        Assert.Equal(new[] { theirs.Id }, (await service.GetAllAsync(_otherUserId)).Select(p => p.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(orphan.Id, UserId));
    }

    [Fact]
    public async Task El_borrado_forzado_solo_elimina_los_movimientos_del_propietario()
    {
        var service = CreateService();
        var pasanaco = await service.CreateAsync(UserId, NewPasanaco("Familia"));
        Db.Expenses.AddRange(
            NewExpense(UserId, $"Cuota del pasanaco {pasanaco.Id}"),
            NewExpense(_otherUserId, $"Nota que menciona {pasanaco.Id}"));
        await Db.SaveChangesAsync();

        await service.DeletePasanacoCascadeAsync(pasanaco.Id, UserId);

        Db.ChangeTracker.Clear();
        Assert.False(await Db.Pasanacos.AnyAsync(p => p.Id == pasanaco.Id));
        var survivor = Assert.Single(await Db.Expenses.ToListAsync());
        Assert.Equal(_otherUserId, survivor.UserId);
    }

    private PasanacoService CreateService()
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
        return new PasanacoService(Db, new LoanService(Db, mapper), new ExpenseService(Db, mapper), new IncomeService(Db, mapper));
    }

    private static CreatePasanacoDto NewPasanaco(string name) => new()
    {
        Name = name,
        MonthlyAmount = 100m,
        TotalParticipants = 5,
        CurrentRound = 1,
        StartMonth = 1,
        StartYear = 2026
    };

    private static Expense NewExpense(Guid userId, string notes) => new()
    {
        UserId = userId,
        Amount = 100m,
        Description = "Cuota",
        Date = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
        CategoryId = DefaultCategories.PersonalLoan,
        Notes = notes
    };
}
