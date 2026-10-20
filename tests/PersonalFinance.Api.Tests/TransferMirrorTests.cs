using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Controllers;
using PersonalFinance.Api.Features.Ledger.Commands.CloseMonth;
using PersonalFinance.Api.Features.Ledger.Commands.SeedChartOfAccounts;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Api.Features.Ledger.Queries.GetMonthlySummary;
using PersonalFinance.Domain.Ledger.Calculations;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Tests;

/// <summary>
/// La hucha de Revolut no tiene extracto propio: sus movimientos se deducen de
/// los traspasos importados en la cuenta principal. Además, el arrastre de un
/// mes rebasado el día 1 no debe contar dos veces la nómina cobrada antes.
/// </summary>
public sealed class TransferMirrorTests : LedgerTestBase
{
    private const string Header =
        "Tipo;Producto;Fecha de inicio;Fecha de finalización;Descripción;Importe;Comisión;Divisa;State;Saldo";

    private const string HuchaConcept = "Hucha y fondos Revolut";

    public TransferMirrorTests() => Clock.Today = new DateOnly(2026, 10, 20);

    private T WithUser<T>(T controller) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, UserId.ToString()) }, "Test"))
            }
        };
        return controller;
    }

    private ImportsController Imports() => WithUser(new ImportsController(
        Db,
        new NullAiSuggestionService(),
        new NullChatService(),
        new ImportBatchApplicationService(Db, Provider.GetRequiredService<PeriodProvisioner>()),
        Mediator));

    private AccountsController Accounts() => WithUser(new AccountsController(Db));

    private async Task<Guid> SeedAccountAsync(decimal openingBalance, DateOnly openingDate, string name)
    {
        var account = new Account
        {
            UserId = UserId,
            Name = name,
            Currency = "EUR",
            OpeningBalance = openingBalance,
            OpeningDate = openingDate
        };
        Db.Accounts.Add(account);
        await Db.SaveChangesAsync();
        return account.Id;
    }

    private async Task<Concept> ConceptAsync(string name) =>
        await Db.Concepts.SingleAsync(concept => concept.UserId == UserId && concept.Name == name);

    private async Task SeedMappingsAsync(Guid accountId, params (string Pattern, string Concept)[] rules)
    {
        foreach (var (pattern, conceptName) in rules)
        {
            Db.ConceptMappings.Add(new ConceptMapping
            {
                UserId = UserId,
                AccountId = accountId,
                Pattern = pattern,
                ConceptId = (await ConceptAsync(conceptName)).Id,
                Priority = 10
            });
        }
        await Db.SaveChangesAsync();
    }

    private static IFormFile BuildCsv(params string[] rows)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join("\n", new[] { Header }.Concat(rows)));
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "extracto.csv");
    }

    private async Task<ActionResult<ImportBatchDto>> UploadAsync(Guid accountId, params string[] rows) =>
        await Imports().Create(accountId, BuildCsv(rows), CancellationToken.None);

    private async Task ImportAndApplyAsync(Guid accountId, params string[] rows)
    {
        var batch = (Assert.IsType<OkObjectResult>((await UploadAsync(accountId, rows)).Result).Value as ImportBatchDto)!;
        Assert.IsType<OkObjectResult>((await Imports().Apply(batch.Id, CancellationToken.None)).Result);
    }

    private async Task<AccountDto> CreateHuchaAsync(decimal openingBalance, Guid? transferConceptId)
    {
        var result = await Accounts().Create(new CreateAccountDto
        {
            Name = "Revolut Hucha",
            Type = AccountType.Savings,
            Currency = "EUR",
            OpeningBalance = openingBalance,
            OpeningDate = new DateOnly(2026, 10, 1),
            TransferConceptId = transferConceptId
        }, CancellationToken.None);
        return (Assert.IsType<OkObjectResult>(result.Result).Value as AccountDto)!;
    }

    private async Task<ActionResult<AccountDto>> UpdateAsync(
        Guid accountId, Guid? transferConceptId = null, decimal? openingBalance = null, DateOnly? openingDate = null)
    {
        var account = await Db.Accounts.AsNoTracking().SingleAsync(item => item.Id == accountId);
        return await Accounts().Update(accountId, new UpdateAccountDto
        {
            Name = account.Name,
            Type = account.Type,
            Currency = account.Currency,
            IsActive = account.IsActive,
            TransferConceptId = transferConceptId,
            OpeningBalance = openingBalance,
            OpeningDate = openingDate
        }, CancellationToken.None);
    }

    private async Task<decimal> BalanceAsync(Guid accountId, DateOnly asOf)
    {
        var account = await Db.Accounts.AsNoTracking().SingleAsync(item => item.Id == accountId);
        var entries = await Db.LedgerEntries.AsNoTracking()
            .Where(entry => entry.UserId == UserId && entry.AccountId == accountId)
            .ToListAsync();
        return AccountBalanceCalculator.ComputeBalance(account, entries, asOf);
    }

    private Task<List<LedgerEntry>> EntriesOfAsync(Guid accountId) =>
        Db.LedgerEntries.AsNoTracking()
            .Where(entry => entry.UserId == UserId && entry.AccountId == accountId)
            .ToListAsync();

    private Task<MonthlySummaryDto> SummaryAsync(int year, int month) =>
        Mediator.Send(new GetMonthlySummaryQuery(UserId, year, month));

    private static readonly string[] OctoberMovements =
    {
        "Transferencia;Actual;05/10/2026 09:00;05/10/2026 09:00;NOMINA;1000.00;0.00;EUR;COMPLETADO;1100.00",
        "Transferencia;Actual;06/10/2026 09:00;06/10/2026 09:00;TO FLEXIBLE CASH FUNDS;-800.00;0.00;EUR;COMPLETADO;300.00",
        "Transferencia;Actual;07/10/2026 09:00;07/10/2026 09:00;FROM FLEXIBLE CASH FUNDS;150.00;0.00;EUR;COMPLETADO;450.00"
    };

    [Fact]
    public async Task Un_traspaso_importado_a_la_hucha_mueve_tambien_el_saldo_de_la_hucha()
    {
        await Mediator.Send(new SeedChartOfAccountsCommand(UserId));
        var revolutId = await SeedAccountAsync(100m, new DateOnly(2026, 10, 1), "Revolut");
        await SeedMappingsAsync(revolutId, ("NOMINA", "Nómina"), ("FLEXIBLE CASH FUNDS", HuchaConcept));
        var hucha = await CreateHuchaAsync(500m, (await ConceptAsync(HuchaConcept)).Id);

        await ImportAndApplyAsync(revolutId, OctoberMovements);

        var mirrors = await EntriesOfAsync(hucha.Id);
        Assert.Equal(2, mirrors.Count);
        Assert.All(mirrors, mirror =>
        {
            Assert.True(mirror.IsTransfer);
            Assert.Null(mirror.Fingerprint);
            Assert.Equal(EntryStatus.Paid, mirror.Status);
        });
        Assert.Equal(EntryDirection.In, mirrors.Single(mirror => mirror.ActualAmount == 800m).Direction);
        Assert.Equal(EntryDirection.Out, mirrors.Single(mirror => mirror.ActualAmount == 150m).Direction);

        var sources = (await EntriesOfAsync(revolutId)).Where(entry => entry.IsTransfer).ToList();
        Assert.Equal(
            sources.Select(entry => entry.ImportRowId).OrderBy(id => id),
            mirrors.Select(entry => entry.ImportRowId).OrderBy(id => id));

        var lastDay = new DateOnly(2026, 10, 31);
        Assert.Equal(450m, await BalanceAsync(revolutId, lastDay));
        Assert.Equal(1150m, await BalanceAsync(hucha.Id, lastDay));

        // Lo que pasa a la hucha sigue siendo dinero de casa: el mes cuadra con la suma de las dos cuentas.
        var october = await SummaryAsync(2026, 10);
        Assert.Equal(600m, october.CarryOverAmount);
        Assert.Equal(1000m, october.Totals.IncomeActual);
        Assert.Equal(0m, october.Totals.ExpenseActual);
        Assert.Equal(1600m, october.Totals.ActualBalance);
    }

    [Fact]
    public async Task Un_traspaso_pendiente_que_llega_completado_confirma_tambien_su_espejo()
    {
        await Mediator.Send(new SeedChartOfAccountsCommand(UserId));
        var revolutId = await SeedAccountAsync(100m, new DateOnly(2026, 10, 1), "Revolut");
        await SeedMappingsAsync(revolutId, ("FLEXIBLE CASH FUNDS", HuchaConcept));
        var hucha = await CreateHuchaAsync(0m, (await ConceptAsync(HuchaConcept)).Id);

        await ImportAndApplyAsync(
            revolutId,
            "Transferencia;Actual;06/10/2026 09:00;;TO FLEXIBLE CASH FUNDS;-40.00;0.00;EUR;PENDIENTE;");
        var pendingMirror = Assert.Single(await EntriesOfAsync(hucha.Id));
        Assert.Equal(EntryStatus.Pending, pendingMirror.Status);
        Assert.Null(pendingMirror.ValueDate);

        var again = await UploadAsync(
            revolutId,
            "Transferencia;Actual;06/10/2026 09:00;06/10/2026 10:00;TO FLEXIBLE CASH FUNDS;-40.00;0.00;EUR;COMPLETADO;60.00");
        Assert.IsType<ConflictObjectResult>(again.Result);

        var mirror = Assert.Single(await EntriesOfAsync(hucha.Id));
        Assert.Equal(EntryStatus.Paid, mirror.Status);
        Assert.Equal(new DateOnly(2026, 10, 6), mirror.ValueDate);
        Assert.Equal(40m, await BalanceAsync(hucha.Id, new DateOnly(2026, 10, 31)));
        Assert.Equal(60m, await BalanceAsync(revolutId, new DateOnly(2026, 10, 31)));
    }

    [Fact]
    public async Task Enlazar_la_hucha_despues_refleja_lo_ya_importado_y_quitar_el_enlace_lo_borra()
    {
        await Mediator.Send(new SeedChartOfAccountsCommand(UserId));
        var revolutId = await SeedAccountAsync(100m, new DateOnly(2026, 10, 1), "Revolut");
        await SeedMappingsAsync(revolutId, ("NOMINA", "Nómina"), ("FLEXIBLE CASH FUNDS", HuchaConcept));
        var huchaConceptId = (await ConceptAsync(HuchaConcept)).Id;

        await ImportAndApplyAsync(revolutId, OctoberMovements);
        var hucha = await CreateHuchaAsync(500m, transferConceptId: null);
        Assert.Empty(await EntriesOfAsync(hucha.Id));

        var linked = Assert.IsType<OkObjectResult>((await UpdateAsync(hucha.Id, huchaConceptId)).Result);
        Assert.Equal(huchaConceptId, (linked.Value as AccountDto)!.TransferConceptId);
        Assert.Equal(2, (await EntriesOfAsync(hucha.Id)).Count);
        Assert.Equal(1150m, await BalanceAsync(hucha.Id, new DateOnly(2026, 10, 31)));

        // Guardar la cuenta sin tocar el enlace no duplica nada.
        Assert.IsType<OkObjectResult>((await UpdateAsync(hucha.Id)).Result);
        Assert.Equal(2, (await EntriesOfAsync(hucha.Id)).Count);

        Assert.IsType<OkObjectResult>((await UpdateAsync(hucha.Id, Guid.Empty)).Result);
        Assert.Empty(await EntriesOfAsync(hucha.Id));
        Assert.Null((await Db.Accounts.AsNoTracking().SingleAsync(item => item.Id == hucha.Id)).TransferConceptId);
        Assert.Equal(3, (await EntriesOfAsync(revolutId)).Count);
    }

    [Fact]
    public async Task Solo_se_puede_enlazar_un_concepto_de_traspaso_y_en_una_sola_cuenta_activa()
    {
        await Mediator.Send(new SeedChartOfAccountsCommand(UserId));
        var hucha = await CreateHuchaAsync(0m, (await ConceptAsync(HuchaConcept)).Id);
        var otherId = await SeedAccountAsync(0m, new DateOnly(2026, 10, 1), "Otra");

        Assert.IsType<BadRequestObjectResult>(
            (await UpdateAsync(otherId, (await ConceptAsync("Nómina")).Id)).Result);
        Assert.IsType<BadRequestObjectResult>(
            (await UpdateAsync(otherId, hucha.TransferConceptId)).Result);
        Assert.IsType<BadRequestObjectResult>(
            (await UpdateAsync(otherId, Guid.NewGuid())).Result);
        Assert.Null((await Db.Accounts.AsNoTracking().SingleAsync(item => item.Id == otherId)).TransferConceptId);
    }

    [Fact]
    public async Task Se_puede_corregir_el_saldo_inicial_y_la_fecha_de_apertura()
    {
        var accountId = await SeedAccountAsync(0m, new DateOnly(2026, 9, 27), "Conjunta");

        var updated = Assert.IsType<OkObjectResult>(
            (await UpdateAsync(accountId, openingBalance: 0.72m, openingDate: new DateOnly(2026, 10, 1))).Result);
        Assert.Equal(0.72m, (updated.Value as AccountDto)!.OpeningBalance);

        // Sin esos campos (como envía hoy la pantalla al renombrar), no se tocan.
        Assert.IsType<OkObjectResult>((await UpdateAsync(accountId)).Result);
        var account = await Db.Accounts.AsNoTracking().SingleAsync(item => item.Id == accountId);
        Assert.Equal(0.72m, account.OpeningBalance);
        Assert.Equal(new DateOnly(2026, 10, 1), account.OpeningDate);
    }

    [Fact]
    public async Task El_arrastre_de_un_mes_rebasado_no_cuenta_dos_veces_la_nomina_cobrada_antes_del_dia_1()
    {
        await Mediator.Send(new SeedChartOfAccountsCommand(UserId));
        var october = new DateOnly(2026, 10, 1);
        var revolutId = await SeedAccountAsync(12.80m, october, "Revolut");
        await SeedAccountAsync(0.72m, october, "Conjunta");
        await SeedAccountAsync(90m, october, "Efectivo");
        await SeedAccountAsync(2308.09m, october, "Revolut Hucha");
        await SeedMappingsAsync(revolutId, ("NOMINA", "Nómina"));
        Db.RecurringRules.Add(new RecurringRule
        {
            UserId = UserId,
            ConceptId = (await ConceptAsync("Nómina")).Id,
            AccountId = revolutId,
            Direction = EntryDirection.In,
            Frequency = RecurrenceFrequency.Monthly,
            DayOfMonth = 1,
            ForecastAmount = 2360m,
            StartDate = october,
            IsActive = true
        });
        await Db.SaveChangesAsync();

        Assert.Equal(2411.61m, (await SummaryAsync(2026, 10)).CarryOverAmount);

        await ImportAndApplyAsync(
            revolutId,
            "Transferencia;Actual;28/09/2026 09:00;28/09/2026 09:00;NOMINA;2361.52;0.00;EUR;COMPLETADO;2374.32");

        // Los 2.411,61 del día 1 ya incluyen la nómina de octubre cobrada el 28/09.
        var summary = await SummaryAsync(2026, 10);
        Assert.Equal(50.09m, summary.CarryOverAmount);
        Assert.Equal(2361.52m, summary.Totals.IncomeActual);
        Assert.Equal(2411.61m, summary.Totals.ActualBalance);
    }

    [Fact]
    public async Task Cerrar_el_mes_anterior_no_pisa_el_arrastre_de_un_mes_rebasado()
    {
        var october = new DateOnly(2026, 10, 1);
        await SeedAccountAsync(18.42m, october, "Revolut");
        await SeedAccountAsync(14.26m, october, "Conjunta");
        await SummaryAsync(2026, 9);
        Assert.Equal(32.68m, (await SummaryAsync(2026, 10)).CarryOverAmount);

        await Mediator.Send(new CloseMonthCommand(UserId, 2026, 9, new CloseMonthDto { ActualClosingBalance = 28.78m }));

        Assert.Equal(32.68m, (await SummaryAsync(2026, 10)).CarryOverAmount);
    }

    [Fact]
    public void El_ajuste_de_inicio_de_mes_mueve_lo_cobrado_o_pagado_fuera_de_su_mes()
    {
        var october = new DateOnly(2026, 10, 1);
        var september = new DateOnly(2026, 9, 1);
        LedgerEntry Paid(EntryDirection direction, decimal amount, DateOnly valueDate, DateOnly dueDate) => new()
        {
            Direction = direction,
            Status = EntryStatus.Paid,
            ForecastAmount = amount,
            ActualAmount = amount,
            ValueDate = valueDate,
            DueDate = dueDate
        };
        var entries = new[]
        {
            Paid(EntryDirection.In, 2361.52m, new DateOnly(2026, 9, 28), october),
            Paid(EntryDirection.Out, 50m, new DateOnly(2026, 10, 2), september.AddDays(24)),
            Paid(EntryDirection.Out, 30m, new DateOnly(2026, 9, 15), september.AddDays(14)),
            Paid(EntryDirection.Out, 20m, new DateOnly(2026, 10, 3), october.AddDays(2)),
            new LedgerEntry
            {
                Direction = EntryDirection.Out,
                Status = EntryStatus.Pending,
                ForecastAmount = 99m,
                DueDate = september
            }
        };

        var adjustment = AccountBalanceCalculator.ComputeMonthStartAdjustment(
            entries, october, entry => new DateOnly(entry.DueDate.Year, entry.DueDate.Month, 1));

        Assert.Equal(-2411.52m, adjustment);
    }
}
