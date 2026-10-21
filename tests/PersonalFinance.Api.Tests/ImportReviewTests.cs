using System.Security.Claims;
using System.Text.Json;
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

/// <summary>
/// Revisión automática del importador: lo que antes había que mirar a mano con el
/// extracto delante (saldo, previstos, adelantos, repetidos).
/// </summary>
public sealed class ImportReviewTests : LedgerTestBase
{
    private const string Header =
        "Tipo;Producto;Fecha de inicio;Fecha de finalización;Descripción;Importe;Comisión;Divisa;State;Saldo";

    private sealed class RecordingChatService : IImportChatService
    {
        public IReadOnlyList<string> Analysis { get; private set; } = Array.Empty<string>();

        public Task<ImportChatResult> AskAsync(
            string userMessage,
            IReadOnlyList<ImportChatMessage> history,
            IReadOnlyList<ImportChatRowContext> rows,
            IReadOnlyList<ImportAiCandidate> concepts,
            IReadOnlyList<string> analysis,
            CancellationToken ct)
        {
            Analysis = analysis;
            return Task.FromResult(new ImportChatResult("ok", Array.Empty<ImportChatChange>(), Array.Empty<string>()));
        }
    }

    private ImportsController CreateController(IImportChatService? chat = null)
    {
        var periods = Provider.GetRequiredService<PeriodProvisioner>();
        var controller = new ImportsController(
            Db,
            new NullAiSuggestionService(),
            chat ?? new NullChatService(),
            new ImportBatchApplicationService(Db, periods),
            Mediator);
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

    private async Task<Concept> SeedChartAndMapAsync(Guid accountId, params (string Pattern, string Concept)[] rules)
    {
        await Mediator.Send(new SeedChartOfAccountsCommand(UserId));
        var concepts = await Db.Concepts.Where(c => c.UserId == UserId).ToListAsync();
        foreach (var (pattern, conceptName) in rules)
        {
            Db.ConceptMappings.Add(new ConceptMapping
            {
                UserId = UserId,
                AccountId = accountId,
                Pattern = pattern,
                ConceptId = concepts.First(c => c.Name == conceptName).Id,
                Priority = 10
            });
        }
        await Db.SaveChangesAsync();
        return concepts.First(c => c.Name == rules[0].Concept);
    }

    private static IFormFile BuildCsv(string fileName, params string[] rows)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join("\n", new[] { Header }.Concat(rows)));
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName);
    }

    private async Task<Guid> CreateBatchAsync(Guid accountId, IFormFile file)
    {
        var result = await CreateController().Create(accountId, file, CancellationToken.None);
        return (Assert.IsType<OkObjectResult>(result.Result).Value as ImportBatchDto)!.Id;
    }

    private async Task<ImportAnalysisDto> ReviewAsync(Guid batchId)
    {
        var result = await CreateController().GetReview(batchId, CancellationToken.None);
        var review = Assert.IsType<ImportReviewDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.NotNull(review.Analysis);
        return review.Analysis!;
    }

    private static RevolutMovement Movement(
        int row, string completedAt, decimal amount, decimal? balance, string product = "Actual", string state = "COMPLETADO") =>
        new(
            row,
            "Pago con tarjeta",
            product,
            DateTime.Parse(completedAt),
            DateTime.Parse(completedAt),
            "COSA",
            amount,
            0m,
            "EUR",
            state,
            balance);

    [Fact]
    public void El_saldo_del_banco_es_el_del_ultimo_movimiento_completado_de_la_cuenta()
    {
        var closing = ImportBatchAnalyzer.FindClosingBalance(
            new[]
            {
                Movement(1, "2026-10-05T10:00:00", -50m, 50m),
                // Mismo instante: el último es el que parte del saldo del otro (60 - 5 = 55).
                Movement(3, "2026-10-06T09:00:00", -5m, 55m),
                Movement(2, "2026-10-06T09:00:00", -10m, 60m),
                // La hucha y lo pendiente no cuentan.
                Movement(4, "2026-10-07T09:00:00", 999m, 999m, product: "Ahorros"),
                Movement(5, "2026-10-08T09:00:00", -1m, null, state: "PENDIENTE")
            },
            "EUR");

        Assert.NotNull(closing);
        Assert.Equal(55m, closing!.Value.Balance);
        Assert.Equal(new DateOnly(2026, 10, 6), closing.Value.Date);
    }

    [Fact]
    public async Task Dice_que_el_saldo_cuadra_antes_y_despues_de_aplicar()
    {
        var accountId = await SeedAccountAsync(100m, new DateOnly(2026, 10, 1), "Revolut");
        await SeedChartAndMapAsync(accountId, ("NAVAS EXPRESS", "Compras personales"), ("ATM CAIXABANK", "Transporte diario"));
        var batchId = await CreateBatchAsync(accountId, BuildCsv(
            "account-statement_2026-10-01_2026-10-20_es-es_1.csv",
            "Pago con tarjeta;Actual;05/10/2026 10:00;05/10/2026 10:00;NAVAS EXPRESS;-50.00;0.00;EUR;COMPLETADO;50.00",
            "Reintegro;Actual;07/10/2026 09:00;07/10/2026 09:00;ATM CAIXABANK;-30.00;2.00;EUR;COMPLETADO;18.00",
            "Pago con tarjeta;Actual;08/10/2026 12:00;;NAVAS EXPRESS;-15.00;0.00;EUR;PENDIENTE;"));

        var before = await ReviewAsync(batchId);
        // El banco cierra en 18, pero el pago PENDIENTE de 15 ya está apartado: la app debe tener 3.
        Assert.Equal(3m, before.BankBalance);
        Assert.Equal(new DateOnly(2026, 10, 8), before.BankBalanceDate);
        Assert.Equal(3m, before.AppBalance);
        Assert.Equal(0m, before.Difference);
        Assert.Equal(ImportBatchAnalyzer.SeverityOk, before.Findings[0].Severity);
        Assert.Equal("El saldo cuadra con el banco", before.Findings[0].Title);
        Assert.Contains(before.Findings, finding => finding.Title == "Pendientes en el banco");

        Assert.IsType<OkObjectResult>((await CreateController().Apply(batchId, CancellationToken.None)).Result);

        var after = await ReviewAsync(batchId);
        Assert.Equal(3m, after.AppBalance);
        Assert.Equal("El saldo cuadra con el banco", after.Findings[0].Title);
        Assert.Single(after.Findings);
    }

    [Fact]
    public async Task Si_falta_un_movimiento_avisa_de_la_diferencia_y_de_que_el_extracto_no_empieza_el_dia_1()
    {
        var accountId = await SeedAccountAsync(100m, new DateOnly(2026, 10, 1), "Revolut");
        await SeedChartAndMapAsync(accountId, ("NAVAS EXPRESS", "Compras personales"));
        // El banco ya venía de 90 (un gasto de 10 del día 2 no está en el archivo).
        var batchId = await CreateBatchAsync(accountId, BuildCsv(
            "account-statement_2026-10-03_2026-10-20_es-es_1.csv",
            "Pago con tarjeta;Actual;05/10/2026 10:00;05/10/2026 10:00;NAVAS EXPRESS;-50.00;0.00;EUR;COMPLETADO;40.00"));

        var analysis = await ReviewAsync(batchId);

        Assert.Equal(50m, analysis.AppBalance);
        Assert.Equal(-10m, analysis.Difference);
        var balance = analysis.Findings[0];
        Assert.Equal(ImportBatchAnalyzer.SeverityWarning, balance.Severity);
        Assert.Equal("El saldo no cuadra con el banco", balance.Title);
        Assert.Contains("10,00 € más que el banco", balance.Detail);
        Assert.Contains("empieza el 03/10", balance.Detail);
    }

    [Fact]
    public async Task Avisa_de_un_apunte_hecho_a_mano_que_repite_un_movimiento_del_banco()
    {
        var accountId = await SeedAccountAsync(100m, new DateOnly(2026, 10, 1), "Revolut");
        var shopping = await SeedChartAndMapAsync(accountId, ("NAVAS EXPRESS", "Compras personales"));
        var october = new MonthlyPeriod { UserId = UserId, Year = 2026, Month = 10, Status = PeriodStatus.Open };
        Db.MonthlyPeriods.Add(october);
        Db.LedgerEntries.Add(new LedgerEntry
        {
            UserId = UserId,
            AccountId = accountId,
            PeriodId = october.Id,
            ConceptId = shopping.Id,
            Direction = EntryDirection.Out,
            Status = EntryStatus.Paid,
            DueDate = new DateOnly(2026, 10, 4),
            ValueDate = new DateOnly(2026, 10, 4),
            ForecastAmount = 50m,
            ActualAmount = 50m,
            Description = "Compra en Navas"
        });
        await Db.SaveChangesAsync();
        var batchId = await CreateBatchAsync(accountId, BuildCsv(
            "account-statement_2026-10-01_2026-10-20_es-es_1.csv",
            "Pago con tarjeta;Actual;05/10/2026 10:00;05/10/2026 10:00;NAVAS EXPRESS;-50.00;0.00;EUR;COMPLETADO;50.00"));

        var analysis = await ReviewAsync(batchId);

        Assert.Equal(50m, analysis.Difference);
        Assert.Contains("Pista: el apunte «Compra en Navas» del 04/10", analysis.Findings[0].Detail);
        var duplicate = Assert.Single(analysis.Findings, finding => finding.Title == "¿Repetido?");
        Assert.Equal(ImportBatchAnalyzer.SeverityWarning, duplicate.Severity);
        Assert.Contains("apuntado a mano", duplicate.Detail);
        Assert.Single(duplicate.RowNumbers);
    }

    [Fact]
    public async Task Avisa_de_un_gasto_de_fin_de_mes_que_puede_ser_el_del_mes_siguiente()
    {
        var accountId = await SeedAccountAsync(100m, new DateOnly(2026, 10, 1), "Revolut");
        var water = await SeedChartAndMapAsync(accountId, ("CANAL ISABEL", "Agua"));
        Db.RecurringRules.Add(new RecurringRule
        {
            UserId = UserId,
            ConceptId = water.Id,
            AccountId = accountId,
            Direction = EntryDirection.Out,
            Frequency = RecurrenceFrequency.Monthly,
            DayOfMonth = 5,
            ForecastAmount = 75m,
            StartDate = new DateOnly(2026, 11, 1),
            IsActive = true
        });
        await Db.SaveChangesAsync();
        var batchId = await CreateBatchAsync(accountId, BuildCsv(
            "account-statement_2026-10-01_2026-10-31_es-es_1.csv",
            "Pago;Actual;30/10/2026 10:00;30/10/2026 10:00;CANAL ISABEL;-74.20;0.00;EUR;COMPLETADO;25.80"));

        var analysis = await ReviewAsync(batchId);

        Assert.Equal("El saldo cuadra con el banco", analysis.Findings[0].Title);
        var advance = Assert.Single(analysis.Findings, finding => finding.Title == "¿Pago adelantado?");
        Assert.Contains("previsto el 05/11", advance.Detail);
        Assert.Contains("noviembre", advance.Detail);
    }

    [Fact]
    public async Task Avisa_si_lo_previsto_esta_en_otra_cuenta()
    {
        var revolut = await SeedAccountAsync(100m, new DateOnly(2026, 10, 1), "Revolut");
        var joint = await SeedAccountAsync(0m, new DateOnly(2026, 10, 1), "Conjunta");
        var water = await SeedChartAndMapAsync(revolut, ("CANAL ISABEL", "Agua"));
        Db.RecurringRules.Add(new RecurringRule
        {
            UserId = UserId,
            ConceptId = water.Id,
            AccountId = joint,
            Direction = EntryDirection.Out,
            Frequency = RecurrenceFrequency.Monthly,
            DayOfMonth = 5,
            ForecastAmount = 75m,
            StartDate = new DateOnly(2026, 10, 1),
            IsActive = true
        });
        await Db.SaveChangesAsync();
        var batchId = await CreateBatchAsync(revolut, BuildCsv(
            "account-statement_2026-10-01_2026-10-20_es-es_1.csv",
            "Pago;Actual;06/10/2026 10:00;06/10/2026 10:00;CANAL ISABEL;-75.00;0.00;EUR;COMPLETADO;25.00"));

        var analysis = await ReviewAsync(batchId);

        var finding = Assert.Single(analysis.Findings, item => item.Title == "Previsto en otra cuenta");
        Assert.Contains("«Conjunta»", finding.Detail);
        Assert.Contains("«Revolut»", finding.Detail);
    }

    [Fact]
    public async Task Avisa_de_movimientos_sin_concepto_y_de_los_adivinados()
    {
        var accountId = await SeedAccountAsync(100m, new DateOnly(2026, 10, 1), "Revolut");
        await SeedChartAndMapAsync(accountId, ("NAVAS EXPRESS", "Compras personales"));
        var batchId = await CreateBatchAsync(accountId, BuildCsv(
            "account-statement_2026-10-01_2026-10-20_es-es_1.csv",
            "Pago con tarjeta;Actual;05/10/2026 10:00;05/10/2026 10:00;TIENDA RARA;-10.00;0.00;EUR;COMPLETADO;90.00",
            "Pago con tarjeta;Actual;06/10/2026 10:00;06/10/2026 10:00;OTRA TIENDA;-5.00;0.00;EUR;COMPLETADO;85.00"));
        var rows = await Db.ImportRows.Where(row => row.BatchId == batchId).OrderBy(row => row.RowNumber).ToListAsync();
        rows[0].SuggestedConceptId = null;
        rows[0].SuggestionSource = null;
        await Db.SaveChangesAsync();

        var analysis = await ReviewAsync(batchId);

        var missing = Assert.Single(analysis.Findings, finding => finding.Title == "Movimientos sin concepto");
        Assert.Equal(new[] { rows[0].RowNumber }, missing.RowNumbers);
        var guessed = Assert.Single(analysis.Findings, finding => finding.Title == "Conceptos adivinados");
        Assert.Equal(new[] { rows[1].RowNumber }, guessed.RowNumbers);
        // Lo que pide hacer algo va antes que lo informativo.
        Assert.True(
            analysis.Findings.ToList().IndexOf(missing) < analysis.Findings.ToList().IndexOf(guessed));
    }

    [Fact]
    public async Task Si_todo_estaba_importado_dice_igualmente_si_el_saldo_cuadra()
    {
        var accountId = await SeedAccountAsync(100m, new DateOnly(2026, 10, 1), "Revolut");
        await SeedChartAndMapAsync(accountId, ("NAVAS EXPRESS", "Compras personales"));
        string[] rows =
        {
            "Pago con tarjeta;Actual;05/10/2026 10:00;05/10/2026 10:00;NAVAS EXPRESS;-50.00;0.00;EUR;COMPLETADO;50.00"
        };
        var batchId = await CreateBatchAsync(accountId, BuildCsv("extracto.csv", rows));
        await CreateController().Apply(batchId, CancellationToken.None);

        var again = await CreateController().Create(accountId, BuildCsv("extracto.csv", rows), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(again.Result);
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(conflict.Value));
        Assert.Contains("El saldo cuadra con el banco", body.RootElement.GetProperty("message").GetString());
        Assert.Equal("ok", body.RootElement.GetProperty("balanceCheck").GetProperty("Severity").GetString());
    }

    [Fact]
    public async Task El_asistente_recibe_la_revision_automatica()
    {
        var accountId = await SeedAccountAsync(100m, new DateOnly(2026, 10, 1), "Revolut");
        await SeedChartAndMapAsync(accountId, ("NAVAS EXPRESS", "Compras personales"));
        var batchId = await CreateBatchAsync(accountId, BuildCsv(
            "extracto.csv",
            "Pago con tarjeta;Actual;05/10/2026 10:00;05/10/2026 10:00;NAVAS EXPRESS;-50.00;0.00;EUR;COMPLETADO;50.00"));
        var chat = new RecordingChatService();

        var result = await CreateController(chat).Chat(
            batchId, new ImportChatRequestDto { Message = "¿Cuadra?" }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Contains(chat.Analysis, line => line.StartsWith("[ok] El saldo cuadra con el banco", StringComparison.Ordinal));
    }
}
