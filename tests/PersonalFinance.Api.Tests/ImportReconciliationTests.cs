using System.Globalization;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
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
    public string? FailureReason => "IA desactivada en pruebas.";

    public Task<IReadOnlyDictionary<string, ImportAiSuggestion>> SuggestAsync(
        IReadOnlyList<string> descriptions, IReadOnlyList<ImportAiCandidate> concepts, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, ImportAiSuggestion>>(
            new Dictionary<string, ImportAiSuggestion>());
}

/// <summary>Chat inerte: no hace falta Anthropic para probar el cuadre contable.</summary>
public sealed class NullChatService : IImportChatService
{
    public Task<ImportChatResult> AskAsync(
        string userMessage,
        IReadOnlyList<ImportChatMessage> history,
        IReadOnlyList<ImportChatRowContext> rows,
        IReadOnlyList<ImportAiCandidate> concepts,
        CancellationToken ct) =>
        Task.FromResult(new ImportChatResult(string.Empty, Array.Empty<ImportChatChange>(), Array.Empty<string>()));
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
        var application = new ImportBatchApplicationService(Db, periods);
        var controller = new ImportsController(
            Db,
            new NullAiSuggestionService(),
            new NullChatService(),
            application,
            Mediator);

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
            ("NAVAS EXPRESS", "Compras personales"),
            ("PAGO GENERICO", "Nómina"),
            ("ATM CAIXABANK", "Transporte diario"),
            ("PAGO PENDIENTE COSA", "Supermercado y alimentación del hogar")
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
        Assert.Equal(64, feeEntry.Fingerprint!.Length);

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
        await SeedMappingForAllAsync(personal, new[] { ("NOMINA", "Nómina") });
        await SeedMappingForAllAsync(conjunta, new[] { ("ALQUILER", "Alquiler") });

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

    [Fact]
    public async Task Importa_nomina_con_importe_real_y_la_empareja_con_prevision_de_octubre()
    {
        await SeedChartOfAccountsAsync();
        var accountId = await SeedAccountAsync(18.42m, new DateOnly(2026, 10, 1), "Revolut");
        await SeedMappingForAllAsync(accountId, new[] { ("NOMINA", "Nómina") });
        var salary = await Db.Concepts.SingleAsync(concept => concept.UserId == UserId && concept.Name == "Nómina");
        Db.RecurringRules.Add(new RecurringRule
        {
            UserId = UserId,
            ConceptId = salary.Id,
            AccountId = accountId,
            Direction = EntryDirection.In,
            Frequency = RecurrenceFrequency.Monthly,
            DayOfMonth = 1,
            ForecastAmount = 2360m,
            StartDate = new DateOnly(2026, 10, 1),
            IsActive = true
        });
        await Db.SaveChangesAsync();

        var batch = (Assert.IsType<OkObjectResult>(
                (await CreateController(UserId).Create(
                    accountId,
                    BuildCsv("Transferencia;Actual;29/09/2026 09:00;29/09/2026 09:00;NOMINA;2361.52;0.00;EUR;COMPLETADO;2379.94"),
                    CancellationToken.None)).Result)
            .Value as ImportBatchDto)!;

        var result = await CreateController(UserId).Apply(batch.Id, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);

        var salaryEntry = await Db.LedgerEntries.SingleAsync(entry =>
            entry.UserId == UserId &&
            entry.ConceptId == salary.Id &&
            entry.AccountId == accountId);
        var account = await Db.Accounts.SingleAsync(item => item.Id == accountId);
        var row = await Db.ImportRows.SingleAsync(item => item.BatchId == batch.Id);

        Assert.Equal(new DateOnly(2026, 10, 1), salaryEntry.DueDate);
        Assert.Equal(new DateOnly(2026, 9, 29), salaryEntry.ValueDate);
        Assert.Equal(EntryStatus.Paid, salaryEntry.Status);
        Assert.Equal(2360m, salaryEntry.ForecastAmount);
        Assert.Equal(2361.52m, salaryEntry.ActualAmount);
        Assert.Equal(row.Id, salaryEntry.ImportRowId);
        Assert.Equal(salaryEntry.Id, row.LedgerEntryId);
        Assert.Equal(2379.94m, account.OpeningBalance);
        Assert.False(await Db.MonthlyPeriods.AnyAsync(period =>
            period.UserId == UserId && period.Year == 2026 && period.Month == 9));
    }

    [Fact]
    public async Task No_aplica_si_una_fila_coincide_con_varias_previsiones_cercanas()
    {
        await SeedChartOfAccountsAsync();
        var accountId = await SeedAccountAsync(18.42m, new DateOnly(2026, 10, 1), "Revolut");
        await SeedMappingForAllAsync(accountId, new[] { ("NOMINA", "Nómina") });
        var salary = await Db.Concepts.SingleAsync(concept => concept.UserId == UserId && concept.Name == "Nómina");
        Db.RecurringRules.AddRange(
            new RecurringRule
            {
                UserId = UserId,
                ConceptId = salary.Id,
                AccountId = accountId,
                Direction = EntryDirection.In,
                Frequency = RecurrenceFrequency.Monthly,
                DayOfMonth = 1,
                ForecastAmount = 2360m,
                StartDate = new DateOnly(2026, 10, 1),
                IsActive = true
            },
            new RecurringRule
            {
                UserId = UserId,
                ConceptId = salary.Id,
                AccountId = accountId,
                Direction = EntryDirection.In,
                Frequency = RecurrenceFrequency.Monthly,
                DayOfMonth = 3,
                ForecastAmount = 2360m,
                StartDate = new DateOnly(2026, 10, 1),
                IsActive = true
            });
        await Db.SaveChangesAsync();

        var batch = (Assert.IsType<OkObjectResult>(
                (await CreateController(UserId).Create(
                    accountId,
                    BuildCsv("Transferencia;Actual;29/09/2026 09:00;29/09/2026 09:00;NOMINA;2361.52;0.00;EUR;COMPLETADO;2379.94"),
                    CancellationToken.None)).Result)
            .Value as ImportBatchDto)!;

        var result = await CreateController(UserId).Apply(batch.Id, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Contains("varias previsiones", conflict.Value!.ToString());
        Assert.Empty(await Db.LedgerEntries.Where(entry => entry.UserId == UserId).ToListAsync());
        Assert.Empty(await Db.MonthlyPeriods.Where(period => period.UserId == UserId).ToListAsync());
    }

    [Fact]
    public async Task Rebasar_una_cuenta_conserva_el_historico_pero_no_lo_suma_al_nuevo_saldo()
    {
        var accountId = await SeedAccountAsync(18.42m, new DateOnly(2026, 10, 1), "Revolut");
        var period = new MonthlyPeriod
        {
            UserId = UserId,
            Year = 2026,
            Month = 9,
            Status = PeriodStatus.Open
        };
        Db.MonthlyPeriods.Add(period);
        var (_, concept) = SeedConcept("Prueba", "Movimiento de prueba", ConceptKind.Expense);
        Db.LedgerEntries.AddRange(
            new LedgerEntry
            {
                UserId = UserId,
                AccountId = accountId,
                PeriodId = period.Id,
                ConceptId = concept.Id,
                Direction = EntryDirection.In,
                Status = EntryStatus.Paid,
                DueDate = new DateOnly(2026, 9, 20),
                ValueDate = new DateOnly(2026, 9, 20),
                ForecastAmount = 500m,
                ActualAmount = 500m
            },
            new LedgerEntry
            {
                UserId = UserId,
                AccountId = accountId,
                PeriodId = period.Id,
                ConceptId = concept.Id,
                Direction = EntryDirection.Out,
                Status = EntryStatus.Paid,
                DueDate = new DateOnly(2026, 10, 2),
                ValueDate = new DateOnly(2026, 10, 2),
                ForecastAmount = 3m,
                ActualAmount = 3m
            });
        await Db.SaveChangesAsync();

        var account = await Db.Accounts.AsNoTracking().SingleAsync(item => item.Id == accountId);
        var entries = await Db.LedgerEntries.AsNoTracking().ToListAsync();

        var balance = PersonalFinance.Domain.Ledger.Calculations.AccountBalanceCalculator.ComputeBalance(
            account, entries, new DateOnly(2026, 10, 31));

        Assert.Equal(15.42m, balance);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task Un_borrador_anterior_no_convierte_el_reintento_en_duplicados()
    {
        await SeedChartOfAccountsAsync();
        var accountId = await SeedAccountAsync(0m, new DateOnly(2026, 1, 1), "Revolut");
        var row =
            "Pago con tarjeta;Actual;05/01/2026 14:22;05/01/2026 14:22;MERCADONA;-50.00;0.00;EUR;COMPLETADO;-50.00";

        var first = Assert.IsType<OkObjectResult>(
            (await CreateController(UserId).Create(
                accountId,
                BuildCsv(row),
                CancellationToken.None)).Result).Value as ImportBatchDto;
        var retry = Assert.IsType<OkObjectResult>(
            (await CreateController(UserId).Create(
                accountId,
                BuildCsv(row),
                CancellationToken.None)).Result).Value as ImportBatchDto;

        Assert.NotNull(first);
        Assert.NotNull(retry);
        Assert.Equal(1, first!.AcceptedRows);
        Assert.Equal(1, retry!.AcceptedRows);
        Assert.Equal(0, retry.DuplicateRows);

        await CreateController(UserId).Apply(first.Id, CancellationToken.None);

        var afterApply = await CreateController(UserId).Create(
            accountId,
            BuildCsv(row),
            CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(afterApply.Result);
    }

    [Fact]
    public async Task Reconoce_el_mismo_movimiento_al_reexportar_un_rango_distinto()
    {
        await SeedChartOfAccountsAsync();
        var accountId = await SeedAccountAsync(0m, new DateOnly(2026, 1, 1), "Revolut");
        await SeedMappingForAllAsync(accountId, new[]
        {
            ("PAGO GENERICO", "Nómina"),
            ("MERCADONA", "Supermercado y alimentación del hogar")
        });
        const string target =
            "Pago con tarjeta;Actual;05/01/2026 14:22;05/01/2026 14:22;MERCADONA;-50.00;0.00;EUR;COMPLETADO;-50.00";

        var firstBatch = Assert.IsType<OkObjectResult>(
            (await CreateController(UserId).Create(
                accountId,
                BuildCsv(
                    "Transferencia;Actual;04/01/2026 09:00;04/01/2026 09:00;PAGO GENERICO;100.00;0.00;EUR;COMPLETADO;100.00",
                    target),
                CancellationToken.None)).Result).Value as ImportBatchDto;
        Assert.NotNull(firstBatch);
        Assert.Equal(2, firstBatch!.AcceptedRows);
        await CreateController(UserId).Apply(firstBatch.Id, CancellationToken.None);
        Assert.Equal(2, await Db.ImportRows.CountAsync(row =>
            row.BatchId == firstBatch.Id &&
            row.Status == ImportRowStatus.Accepted &&
            row.LedgerEntryId != null));
        var savedTargetRow = await Db.ImportRows.SingleAsync(row =>
            row.BatchId == firstBatch.Id && row.RawDescription == "MERCADONA");
        var parsedTarget = RevolutCsvParser.Parse(new StringReader(string.Join("\n", Header, target)))
            .Movements.Single();
        var savedMovementKey = string.Join(
            "|",
            savedTargetRow.ValueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            savedTargetRow.Amount.ToString("G29", CultureInfo.InvariantCulture),
            savedTargetRow.Fee.ToString("G29", CultureInfo.InvariantCulture),
            savedTargetRow.Currency?.Trim().ToUpperInvariant(),
            savedTargetRow.NormalizedDescription);
        var parsedMovementKey = string.Join(
            "|",
            DateOnly.FromDateTime(parsedTarget.StartedAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            parsedTarget.Amount.ToString("G29", CultureInfo.InvariantCulture),
            parsedTarget.Fee.ToString("G29", CultureInfo.InvariantCulture),
            parsedTarget.Currency.Trim().ToUpperInvariant(),
            RevolutMovementClassifier.Normalize(parsedTarget.Description));
        Assert.Equal(savedMovementKey, parsedMovementKey);
        var importedRowCount = await (
            from row in Db.ImportRows
            join batch in Db.ImportBatches on row.BatchId equals batch.Id
            where row.UserId == UserId &&
                  row.Status == ImportRowStatus.Accepted &&
                  row.LedgerEntryId != null &&
                  batch.AccountId == accountId &&
                  batch.Source == ImportSource.RevolutCsv
            select row.Id)
            .CountAsync();
        Assert.Equal(2, importedRowCount);
        var importedRows = await Db.ImportRows
            .Where(row => row.BatchId == firstBatch.Id)
            .ToListAsync();
        Assert.Equal(1, importedRows.Count(row => string.Join(
            "|",
            row.ValueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            row.Amount.ToString("G29", CultureInfo.InvariantCulture),
            row.Fee.ToString("G29", CultureInfo.InvariantCulture),
            row.Currency?.Trim().ToUpperInvariant(),
            row.NormalizedDescription) == parsedMovementKey));

        var importedTarget = await Db.LedgerEntries.SingleAsync(entry =>
            entry.UserId == UserId &&
            entry.AccountId == accountId &&
            entry.Description == "MERCADONA");
        var oldFingerprintInput = string.Join(
            "|",
            accountId.ToString("N"),
            new DateTime(2026, 1, 5, 14, 22, 0).ToString("O", CultureInfo.InvariantCulture),
            parsedTarget.Amount.ToString(CultureInfo.InvariantCulture),
            RevolutMovementClassifier.Normalize("MERCADONA"),
            "3");
        importedTarget.Fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(oldFingerprintInput))).ToLowerInvariant();
        savedTargetRow.LedgerEntryId = null;
        importedTarget.ImportRowId = null;
        await Db.SaveChangesAsync();

        var reexportResult = await CreateController(UserId).Create(
            accountId,
            BuildCsv(
                "Transferencia;Actual;04/01/2026 09:00;04/01/2026 09:00;PAGO GENERICO;100.00;0.00;EUR;COMPLETADO;100.00",
                target,
                target),
            CancellationToken.None);

        var reexport = Assert.IsType<OkObjectResult>(reexportResult.Result).Value as ImportBatchDto;
        Assert.NotNull(reexport);
        Assert.Equal(1, reexport!.AcceptedRows);
        Assert.Equal(2, reexport.DuplicateRows);
        Assert.Equal(2, await Db.LedgerEntries.CountAsync(entry =>
            entry.UserId == UserId && entry.AccountId == accountId));
    }

    [Fact]
    public async Task Sin_IA_ni_mappings_clasifica_todas_las_filas_con_reglas_generales()
    {
        await SeedChartOfAccountsAsync();
        var accountId = await SeedAccountAsync(0m, new DateOnly(2026, 1, 1), "Revolut");
        var file = BuildCsv(
            "Transferencia;Actual;01/01/2026 09:00;01/01/2026 09:00;Transferencia de una persona;100.00;0.00;EUR;COMPLETADO;100.00",
            "Transferencia;Actual;02/01/2026 09:00;02/01/2026 09:00;Bizum payment to: una persona;-20.00;0.00;EUR;COMPLETADO;80.00",
            "Reintegro;Actual;03/01/2026 09:00;03/01/2026 09:00;Retirada de efectivo en cajero;-30.00;0.00;EUR;COMPLETADO;50.00",
            "Pago con tarjeta;Actual;04/01/2026 09:00;04/01/2026 09:00;Comercio desconocido;-5.00;0.00;EUR;COMPLETADO;45.00");

        var createResult = await CreateController(UserId).Create(accountId, file, CancellationToken.None);
        var batch = Assert.IsType<OkObjectResult>(createResult.Result).Value as ImportBatchDto;
        Assert.NotNull(batch);

        var rows = await Db.ImportRows
            .AsNoTracking()
            .Include(row => row.SuggestedConcept)
            .OrderBy(row => row.RowNumber)
            .ToListAsync();

        Assert.All(rows, row => Assert.NotNull(row.SuggestedConceptId));
        Assert.All(rows, row => Assert.Contains(row.SuggestionSource, new[] { "mapping", "regla general" }));
        Assert.Equal(
            new[]
            {
                "Ingresos adicionales",
                "Transferencias y Bizum enviados",
                "Retiradas de efectivo",
                "Compras personales"
            },
            rows.Select(row => row.SuggestedConcept!.Name));

        var pendingRows = await Db.ImportRows.ToListAsync();
        foreach (var row in pendingRows)
        {
            row.SuggestedConceptId = null;
            row.SuggestionSource = null;
            row.SuggestionConfidence = null;
        }
        await Db.SaveChangesAsync();

        var suggestResult = await CreateController(UserId).SuggestPending(
            batch!.Id,
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(suggestResult.Result);
        Assert.All(
            await Db.ImportRows.AsNoTracking().ToListAsync(),
            row => Assert.NotNull(row.SuggestedConceptId));
    }

    [Fact]
    public async Task Preparar_reglas_ignora_conceptos_antiguos_inactivos_con_el_mismo_nombre()
    {
        await SeedChartOfAccountsAsync();
        var activePasanaco = await Db.Concepts.SingleAsync(
            concept => concept.Name == "Pasanaco" && concept.IsActive);
        var legacyGroup = new ConceptGroup
        {
            UserId = UserId,
            Name = "Legacy Pasanaco",
            Kind = ConceptKind.Expense,
            IsActive = false
        };
        Db.ConceptGroups.Add(legacyGroup);
        Db.Concepts.Add(new Concept
        {
            UserId = UserId,
            GroupId = legacyGroup.Id,
            Name = "Pasanaco",
            Kind = ConceptKind.Expense,
            IsActive = false
        });
        await Db.SaveChangesAsync();

        var result = await CreateController(UserId).SeedMappings(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.True(await Db.ConceptMappings.AnyAsync(item => item.Pattern == "MERCADONA"));
        Assert.Equal(2, await Db.Concepts.CountAsync(concept => concept.Name == "Pasanaco"));
        Assert.NotEqual(Guid.Empty, activePasanaco.Id);
    }
}
