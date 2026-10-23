using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Models.Dtos.Bank;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services;

namespace PersonalFinance.Api.Tests;

/// <summary>Las conciliaciones y los bancos de otro usuario se tratan como inexistentes.</summary>
public sealed class ReconciliationOwnershipTests : LedgerTestBase
{
    [Fact]
    public async Task Otro_usuario_no_puede_marcar_una_conciliacion_ajena()
    {
        var reconciliation = SeedReconciliation(SeedBank());
        var service = CreateService(Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(() => service.MarkReconciledAsync(reconciliation.Id));

        Db.ChangeTracker.Clear();
        var stored = await Db.Reconciliations.SingleAsync(r => r.Id == reconciliation.Id);
        Assert.False(stored.Reconciled);
        Assert.Null(stored.ReconciledAt);
    }

    [Fact]
    public async Task El_propietario_puede_marcar_su_conciliacion_cuadrada()
    {
        var reconciliation = SeedReconciliation(SeedBank());
        var service = CreateService(UserId);

        Assert.True(await service.MarkReconciledAsync(reconciliation.Id));

        Db.ChangeTracker.Clear();
        Assert.True((await Db.Reconciliations.SingleAsync(r => r.Id == reconciliation.Id)).Reconciled);
    }

    [Fact]
    public async Task No_se_puede_crear_una_conciliacion_sobre_un_banco_ajeno()
    {
        var bank = SeedBank();
        var service = CreateService(Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.CreateAsync(new CreateReconciliationDto(bank.Id, 2026, 1, 500m, null)));

        Assert.False(await Db.Reconciliations.AnyAsync());
    }

    private ReconciliationService CreateService(Guid userId) =>
        new(Db, new FixedHttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) },
                    "Test"))
            }
        });

    private Bank SeedBank()
    {
        var bank = new Bank { Id = Guid.NewGuid(), UserId = UserId, Name = "BBVA" };
        Db.Banks.Add(bank);
        Db.SaveChanges();
        return bank;
    }

    private Reconciliation SeedReconciliation(Bank bank)
    {
        var reconciliation = new Reconciliation
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            BankId = bank.Id,
            Year = 2026,
            Month = 1,
            ClosingBalance = 0m
        };
        Db.Reconciliations.Add(reconciliation);
        Db.SaveChanges();
        return reconciliation;
    }

    /// <summary>
    /// HttpContextAccessor guarda el contexto en un AsyncLocal estático; uno
    /// propio evita que dos servicios del mismo test compartan usuario.
    /// </summary>
    private sealed class FixedHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
