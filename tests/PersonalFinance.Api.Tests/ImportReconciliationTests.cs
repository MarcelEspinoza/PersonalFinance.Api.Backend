using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Controllers;
using PersonalFinance.Api.Features.Ledger.Commands.SeedChartOfAccounts;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Tests;

/// <summary>Cero sugerencias de IA: el test cubre sólo mapping/manual, sin depender de Anthropic.</summary>
public sealed class NullAiSuggestionService : IImportAiSuggestionService
{
    public Task<IReadOnlyDictionary<string, ImportAiSuggestion>> SuggestAsync(
        IReadOnlyList<string> descriptions, IReadOnlyList<ImportAiCandidate> concepts, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, ImportAiSuggestion>>(
            new Dictionary<string, ImportAiSuggestion>());
}

/// <summary>
/// Reproduce, con un extracto sintético, los cinco bloqueantes detectados por
/// la validación contable: arrastre ignorado, colisión de huella entre
/// movimientos distintos, comisiones no contabilizadas, estado forzado a
/// pagado y saldo no separable por cuenta.
/// </summary>
public sealed class ImportReconciliationTests : LedgerTestBase
{
    private const string Header =
        "Tipo;Producto;Fecha de inicio;Fecha de finalización;Descripción;Importe;Comisión;Divisa;State;Saldo";

    private ImportsController CreateController(Guid userId)
    {
        var periods = Provider.GetRequiredService<PeriodProvisioner>();
        var controller = new ImportsController(Db, new NullAiSuggestionService(), periods);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "Test"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

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

    private async Task SeedChartOfAccountsAsync() =>
        await Mediator.Send(new SeedChartOfAccountsCommand(UserId));

    private async Task SeedMappingForAllAsync(Guid accountId, IEnumerable<(string Pattern, string Concept)> rules)
    {
        var concepts = await Db.Concepts.Where(c => c.UserId == UserId).ToListAsync();
        foreach (var (pattern, conceptName) in rules)
        {
            var concept = concepts.First(c => c.Name == conceptName);
            Db.ConceptMappings.Add(new ConceptMapping
            {
                UserId = UserId,
                AccountId = accountId,
                Pattern = pattern,
                ConceptId = concept.Id,
                Priority = 10
            });
        }
        await Db.SaveChangesAsync();
    }

    private static IFormFile BuildCsv(params string[] rows)
    {
        var content = string.Join("\n", new[] { Header }.Concat(rows));
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", "extracto.csv");
    }

    [Fact]
    public async Task El_saldo_por_cuenta_cuadra_con_arrastre_duplicados_comisiones_y_pendientes()
    {
        await SeedChartOfAccountsAsync();
        var accountId = await SeedAccountAsync(100.00m, new DateOnly(2026, 1, 1), "Personal");
        await SeedMappingForAllAsync(accountId, new[]
        {
            ("NAVAS EXPRESS", "Personal costs"),
            ("PAGO GENERICO", "My Salary"),
            ("ATM CAIXABANK", "Transport"),
            ("PAGO PENDIENTE COSA", "Food & Beverage")
        });

        var file = BuildCsv(
            // Arrastre: 100,00 de apertura, comprobado al final.
            "Pago con tarjeta;Actual;05/01/2026 14:22;05/01/2026 14:22;NAVAS EXPRESS;-50.00;0.00;EUR;COMPLETADO;50.00",
            // Dos movimientos DISTINTOS con misma fecha/importe/descripción: sin
            // el número de fila en la huella, el segundo se perdería como
            // "duplicado" aunque el saldo bancario demuestra que son dos.
            "Transferencia;Actual;06/01/2026 10:00;06/01/2026 10:00;PAGO GENERICO;20.00;0.00;EUR;COMPLETADO;70.00",
            "Transferencia;Actual;06/01/2026 10:00;06/01/2026 10:00;PAGO GENERICO;20.00;0.00;EUR;COMPLETADO;90.00",
            // Comisión bancaria: el banco resta importe y comisión por separado.
            "Reintegro;Actual;07/01/2026 09:00;07/01/2026 09:00;ATM CAIXABANK;-30.00;2.00;EUR;COMPLETADO;58.00",
            // Pendiente de liquidar: no debe contar en el saldo real todavía.
            "Pago con tarjeta;Actual;24/09/2026 12:00;;PAGO PENDIENTE COSA;-15.00;0.00;EUR;PENDIENTE;");

        var createResult = await CreateController(UserId).Create(accountId, file, CancellationToken.None);
        var batch = Assert.IsType<OkObjectResult>(createResult.Result).Value as ImportBatchDto;
        Assert.NotNull(batch);
        Assert.Equal(5, batch!.AcceptedRows);
        Assert.Equal(0, batch.DuplicateRows);

        var applyController = CreateController(UserId);
        var applyResult = await applyController.Apply(batch.Id, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(applyResult.Result);

        // Saldo esperado: 100,00 (apertura)
        //   - 50,00 (NAVAS EXPRESS)
        //   + 20,00 + 20,00 (los dos PAGO GENERICO, no colapsados)
        //   - 30,00 - 2,00 (reintegro + su comisión)
        //   = 58,00. El PENDIENTE (-15,00) no entra: aún no está liquidado.
        var account = await Db.Accounts.AsNoTracking().FirstAsync(a => a.Id == accountId);
        var entries = await Db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.UserId == UserId && e.AccountId == accountId && e.Status == EntryStatus.Paid)
            .ToListAsync();

        var balance = PersonalFinance.Domain.Ledger.Calculations.AccountBalanceCalculator.ComputeBalance(
            account, entries, new DateOnly(2026, 9, 30));

        Assert.Equal(58.00m, balance);

        // La comisión quedó como su propio asiento contra "Bank fees".
        var feeEntry = entries.Single(e => e.Description!.StartsWith("Comisión"));
        Assert.Equal(2.00m, feeEntry.ActualAmount);
        Assert.False(feeEntry.IsTransfer);

        // El PENDIENTE se guardó como Pending, sin importe real, y no en la lista "Paid".
        var pendingRow = (await Db.ImportRows.AsNoTracking().ToListAsync())
            .Single(r => r.RawDescription == "PAGO PENDIENTE COSA");
        Assert.Equal(EntryStatus.Pending, pendingRow.ClassifiedStatus);
        Assert.DoesNotContain(entries, e => e.Description == "PAGO PENDIENTE COSA");
    }

    [Fact]
    public async Task Cada_cuenta_mantiene_su_propio_saldo_aunque_compartan_periodo()
    {
        await SeedChartOfAccountsAsync();
        var personal = await SeedAccountAsync(10.52m, new DateOnly(2026, 1, 1), "Personal");
        var conjunta = await SeedAccountAsync(0.00m, new DateOnly(2026, 1, 1), "Conjunta");
        await SeedMappingForAllAsync(personal, new[] { ("NOMINA", "My Salary") });
        await SeedMappingForAllAsync(conjunta, new[] { ("ALQUILER", "Rent Aparment") });

        var filePersonal = BuildCsv(
            "Transferencia;Actual;05/09/2026 09:00;05/09/2026 09:00;NOMINA;18.26;0.00;EUR;COMPLETADO;28.78");
        var fileConjunta = BuildCsv(
            "Pago;Actual;05/09/2026 09:00;05/09/2026 09:00;ALQUILER;14.26;0.00;EUR;COMPLETADO;14.26");

        var batchPersonal = (Assert.IsType<OkObjectResult>(
                (await CreateController(UserId).Create(personal, filePersonal, CancellationToken.None)).Result)
            .Value as ImportBatchDto)!;
        await CreateController(UserId).Apply(batchPersonal.Id, CancellationToken.None);

        var batchConjunta = (Assert.IsType<OkObjectResult>(
                (await CreateController(UserId).Create(conjunta, fileConjunta, CancellationToken.None)).Result)
            .Value as ImportBatchDto)!;
        await CreateController(UserId).Apply(batchConjunta.Id, CancellationToken.None);

        var accounts = await Db.Accounts.AsNoTracking().ToDictionaryAsync(a => a.Id);
        var allEntries = await Db.LedgerEntries.AsNoTracking()
            .Where(e => e.UserId == UserId && e.Status == EntryStatus.Paid)
            .ToListAsync();
        var asOf = new DateOnly(2026, 9, 30);

        var saldoPersonal = PersonalFinance.Domain.Ledger.Calculations.AccountBalanceCalculator.ComputeBalance(
            accounts[personal], allEntries, asOf);
        var saldoConjunta = PersonalFinance.Domain.Ledger.Calculations.AccountBalanceCalculator.ComputeBalance(
            accounts[conjunta], allEntries, asOf);

        Assert.Equal(28.78m, saldoPersonal);
        Assert.Equal(14.26m, saldoConjunta);
    }
}
