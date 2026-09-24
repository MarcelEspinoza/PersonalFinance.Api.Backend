using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Features.Ledger.Commands.CloseMonth;
using PersonalFinance.Api.Features.Ledger.Commands.ConfirmLedgerEntry;
using PersonalFinance.Api.Features.Ledger.Commands.CreateLedgerEntry;
using PersonalFinance.Api.Features.Ledger.Commands.DeleteLedgerEntry;
using PersonalFinance.Api.Features.Ledger.Commands.ReopenMonth;
using PersonalFinance.Api.Features.Ledger.Commands.SkipLedgerEntry;
using PersonalFinance.Api.Features.Ledger.Commands.UnconfirmLedgerEntry;
using PersonalFinance.Api.Features.Ledger.Commands.UpdateLedgerEntry;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;
using PersonalFinance.Api.Features.Ledger.Queries.GetMonthlySummary;

namespace PersonalFinance.Api.Tests;

public class MonthlyLedgerTests : LedgerTestBase
{
    private Task<MonthlySummaryDto> Summary(int year, int month, Guid? user = null) =>
        Mediator.Send(new GetMonthlySummaryQuery(user ?? UserId, year, month));

    private Task<MonthlyEntryDto> Create(CreateLedgerEntryDto dto, Guid? user = null) =>
        Mediator.Send(new CreateLedgerEntryCommand(user ?? UserId, dto));

    private static List<MonthlyEntryDto> Expenses(MonthlySummaryDto s) =>
        s.ExpenseGroups.SelectMany(g => g.Concepts).SelectMany(c => c.Entries).ToList();

    // ----------------------------------------------------------------------
    // Apertura del mes y arrastre
    // ----------------------------------------------------------------------

    [Fact]
    public async Task Abrir_un_mes_sin_historial_arranca_con_arrastre_cero()
    {
        var summary = await Summary(2026, 1);

        Assert.Equal(0m, summary.CarryOverAmount);
        Assert.Equal(PeriodStatus.Open, summary.Status);
        Assert.Equal(2026, summary.Year);
        Assert.Equal(1, summary.Month);
    }

    [Fact]
    public async Task El_arrastre_inicial_sale_del_saldo_de_apertura_de_las_cuentas()
    {
        Db.Accounts.Add(new Account
        {
            UserId = UserId,
            Name = "Revolut",
            OpeningBalance = 1234.56m,
            OpeningDate = new DateOnly(2025, 6, 30)
        });
        await Db.SaveChangesAsync();

        var summary = await Summary(2025, 7);

        Assert.Equal(1234.56m, summary.CarryOverAmount);
    }

    [Fact]
    public async Task Un_mes_sin_validez_se_rechaza()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Summary(2026, 13));
    }

    // ----------------------------------------------------------------------
    // Reglas recurrentes
    // ----------------------------------------------------------------------

    [Fact]
    public async Task Las_reglas_recurrentes_se_materializan_una_sola_vez()
    {
        var (_, concept) = SeedConcept("Subscriptions", "Netflix", ConceptKind.Expense);

        Db.RecurringRules.Add(new RecurringRule
        {
            UserId = UserId,
            ConceptId = concept.Id,
            Direction = EntryDirection.Out,
            Frequency = RecurrenceFrequency.Monthly,
            DayOfMonth = 5,
            ForecastAmount = 13.99m,
            StartDate = new DateOnly(2026, 1, 1),
            Description = "Netflix"
        });
        await Db.SaveChangesAsync();

        await Summary(2026, 3);
        var second = await Summary(2026, 3);

        var entries = Expenses(second);

        Assert.Single(entries);
        Assert.Equal(13.99m, entries[0].ForecastAmount);
        Assert.True(entries[0].FromRecurringRule);
        Assert.Equal(new DateOnly(2026, 3, 5), entries[0].DueDate);
    }

    [Fact]
    public async Task Una_regla_que_cae_en_dia_31_se_recorta_al_ultimo_dia_del_mes()
    {
        var (_, concept) = SeedConcept("Apartment costs", "Rent", ConceptKind.Expense);

        Db.RecurringRules.Add(new RecurringRule
        {
            UserId = UserId,
            ConceptId = concept.Id,
            Direction = EntryDirection.Out,
            Frequency = RecurrenceFrequency.Monthly,
            DayOfMonth = 31,
            ForecastAmount = 700m,
            StartDate = new DateOnly(2026, 1, 1)
        });
        await Db.SaveChangesAsync();

        var summary = await Summary(2026, 2);

        Assert.Equal(new DateOnly(2026, 2, 28), Expenses(summary).Single().DueDate);
    }

    [Fact]
    public async Task Una_regla_trimestral_solo_aparece_cada_tres_meses()
    {
        var (_, concept) = SeedConcept("Extra Fixed Payments", "Seguro Caser", ConceptKind.Expense);

        Db.RecurringRules.Add(new RecurringRule
        {
            UserId = UserId,
            ConceptId = concept.Id,
            Direction = EntryDirection.Out,
            Frequency = RecurrenceFrequency.Quarterly,
            DayOfMonth = 10,
            ForecastAmount = 21.55m,
            StartDate = new DateOnly(2026, 1, 10)
        });
        await Db.SaveChangesAsync();

        Assert.Single(Expenses(await Summary(2026, 1)));
        Assert.Empty(Expenses(await Summary(2026, 2)));
        Assert.Single(Expenses(await Summary(2026, 4)));
    }

    [Fact]
    public async Task Una_prevision_vencida_pasa_a_pendiente()
    {
        var (_, concept) = SeedConcept("Subscriptions", "Netflix", ConceptKind.Expense);

        await Create(new CreateLedgerEntryDto
        {
            ConceptId = concept.Id,
            Direction = EntryDirection.Out,
            DueDate = new DateOnly(2026, 1, 20),
            ForecastAmount = 13.99m
        });

        Assert.Equal(EntryStatus.Planned, Expenses(await Summary(2026, 1)).Single().Status);

        // El día 25 la fecha de cargo ya ha pasado.
        Clock.Today = new DateOnly(2026, 1, 25);

        Assert.Equal(EntryStatus.Pending, Expenses(await Summary(2026, 1)).Single().Status);
    }

    // ----------------------------------------------------------------------
    // Totales
    // ----------------------------------------------------------------------

    [Fact]
    public async Task Lo_previsto_no_cuenta_como_saldo_real_pero_si_como_proyectado()
    {
        var (_, salary) = SeedConcept("INCOMES", "My Salary", ConceptKind.Income);
        var (_, rent) = SeedConcept("Apartment costs", "Rent", ConceptKind.Expense);

        await Create(new CreateLedgerEntryDto
        {
            ConceptId = salary.Id,
            Direction = EntryDirection.In,
            DueDate = new DateOnly(2030, 5, 25),
            ForecastAmount = 2000m,
            ActualAmount = 2000m
        });

        // Sin importe real: sólo previsión.
        await Create(new CreateLedgerEntryDto
        {
            ConceptId = rent.Id,
            Direction = EntryDirection.Out,
            DueDate = new DateOnly(2030, 5, 1),
            ForecastAmount = 700m
        });

        var summary = await Summary(2030, 5);

        Assert.Equal(2000m, summary.Totals.ActualBalance);
        Assert.Equal(1300m, summary.Totals.ProjectedBalance);
        Assert.Equal(700m, summary.Totals.PendingExpense);
    }

    [Fact]
    public async Task Un_asiento_descartado_no_suma_en_ningun_total()
    {
        var (_, concept) = SeedConcept("Subscriptions", "Spotify", ConceptKind.Expense);

        var created = await Create(new CreateLedgerEntryDto
        {
            ConceptId = concept.Id,
            Direction = EntryDirection.Out,
            DueDate = new DateOnly(2030, 6, 5),
            ForecastAmount = 10.99m
        });

        await Mediator.Send(new SkipLedgerEntryCommand(UserId, created.Id, true));

        var summary = await Summary(2030, 6);

        Assert.Equal(0m, summary.Totals.ExpenseForecast);

        // El concepto sigue en el cuadro y el asiento se ve, pero no suma.
        var concepto = summary.ExpenseGroups
            .Single(g => g.Name == "Subscriptions").Concepts
            .Single(c => c.Name == "Spotify");

        Assert.Equal(0m, concepto.ForecastTotal);
        Assert.Equal(EntryStatus.Skipped, Assert.Single(concepto.Entries).Status);
    }

    [Fact]
    public async Task Un_mes_sin_apuntes_devuelve_igualmente_el_plan_de_cuentas()
    {
        SeedConcept("Subscriptions", "Netflix", ConceptKind.Expense);

        var summary = await Summary(2030, 7);

        var concepto = summary.ExpenseGroups
            .Single(g => g.Name == "Subscriptions").Concepts
            .Single(c => c.Name == "Netflix");

        Assert.Empty(concepto.Entries);
        Assert.Equal(0m, concepto.ForecastTotal);
        Assert.Equal(0m, summary.Totals.ExpenseForecast);
    }

    [Fact]
    public async Task Deshacer_el_descarte_devuelve_el_asiento_a_los_totales()
    {
        var (_, concept) = SeedConcept("Subscriptions", "Spotify", ConceptKind.Expense);

        var created = await Create(new CreateLedgerEntryDto
        {
            ConceptId = concept.Id,
            Direction = EntryDirection.Out,
            DueDate = new DateOnly(2030, 6, 5),
            ForecastAmount = 10.99m
        });

        await Mediator.Send(new SkipLedgerEntryCommand(UserId, created.Id, true));
        await Mediator.Send(new SkipLedgerEntryCommand(UserId, created.Id, false));

        Assert.Equal(10.99m, (await Summary(2030, 6)).Totals.ExpenseForecast);
    }

    [Fact]
    public async Task El_presupuesto_del_mes_marca_el_concepto_que_se_pasa()
    {
        var (_, concept) = SeedConcept("Daily expenses", "Supermarket", ConceptKind.Expense);

        Db.MonthlyBudgets.Add(new MonthlyBudget
        {
            UserId = UserId,
            ConceptId = concept.Id,
            Year = 2030,
            Month = 9,
            LimitAmount = 300m
        });
        await Db.SaveChangesAsync();

        await Create(new CreateLedgerEntryDto
        {
            ConceptId = concept.Id,
            Direction = EntryDirection.Out,
            DueDate = new DateOnly(2030, 9, 10),
            ForecastAmount = 300m,
            ActualAmount = 412.30m
        });

        var line = Expenses(await Summary(2030, 9));
        var summary = await Summary(2030, 9);
        var conceptLine = summary.ExpenseGroups.SelectMany(g => g.Concepts).Single();

        Assert.Single(line);
        Assert.Equal(300m, conceptLine.BudgetLimit);
        Assert.True(conceptLine.IsOverBudget);
    }

    // ----------------------------------------------------------------------
    // Cierre y reapertura
    // ----------------------------------------------------------------------

    [Fact]
    public async Task El_cierre_calcula_el_saldo_con_importes_reales_y_lo_arrastra_al_mes_siguiente()
    {
        var (_, salary) = SeedConcept("INCOMES", "My Salary", ConceptKind.Income);
        var (_, rent) = SeedConcept("Apartment costs", "Rent", ConceptKind.Expense);

        var income = await Create(new CreateLedgerEntryDto
        {
            ConceptId = salary.Id,
            Direction = EntryDirection.In,
            DueDate = new DateOnly(2026, 1, 25),
            ForecastAmount = 2000m,
            ActualAmount = 2050m
        });

        await Create(new CreateLedgerEntryDto
        {
            ConceptId = rent.Id,
            Direction = EntryDirection.Out,
            DueDate = new DateOnly(2026, 1, 1),
            ForecastAmount = 700m,
            ActualAmount = 700m
        });

        Assert.Equal(EntryStatus.Paid, income.Status);

        var closed = await Mediator.Send(new CloseMonthCommand(UserId, 2026, 1, new CloseMonthDto()));

        Assert.Equal(PeriodStatus.Closed, closed.Status);
        Assert.Equal(1350m, closed.Totals.ActualBalance);

        Assert.Equal(1350m, (await Summary(2026, 2)).CarryOverAmount);
    }

    [Fact]
    public async Task El_saldo_declarado_al_cerrar_manda_sobre_el_calculado_y_deja_rastro()
    {
        var (_, salary) = SeedConcept("INCOMES", "My Salary", ConceptKind.Income);

        await Create(new CreateLedgerEntryDto
        {
            ConceptId = salary.Id,
            Direction = EntryDirection.In,
            DueDate = new DateOnly(2026, 1, 25),
            ForecastAmount = 2000m,
            ActualAmount = 2000m
        });

        await Mediator.Send(new CloseMonthCommand(
            UserId, 2026, 1, new CloseMonthDto { ActualClosingBalance = 1987.43m }));

        Assert.Equal(1987.43m, (await Summary(2026, 2)).CarryOverAmount);

        var mismatch = Assert.Single(Audit.Mismatches);
        Assert.Equal(1987.43m, mismatch.Declared);
        Assert.Equal(2000m, mismatch.Computed);
    }

    [Fact]
    public async Task Un_mes_cerrado_rechaza_nuevos_asientos_hasta_reabrirlo()
    {
        var (_, concept) = SeedConcept("Subscriptions", "Disney+", ConceptKind.Expense);

        await Summary(2026, 1);
        await Mediator.Send(new CloseMonthCommand(UserId, 2026, 1, new CloseMonthDto()));

        var dto = new CreateLedgerEntryDto
        {
            ConceptId = concept.Id,
            Direction = EntryDirection.Out,
            DueDate = new DateOnly(2026, 1, 15),
            ForecastAmount = 8.99m
        };

        await Assert.ThrowsAsync<BusinessRuleException>(() => Create(dto));

        await Mediator.Send(new ReopenMonthCommand(UserId, 2026, 1));

        Assert.Equal(8.99m, (await Create(dto)).ForecastAmount);
    }

    [Fact]
    public async Task No_se_reabre_un_mes_si_el_siguiente_ya_esta_cerrado()
    {
        await Summary(2026, 1);
        await Summary(2026, 2);

        await Mediator.Send(new CloseMonthCommand(UserId, 2026, 1, new CloseMonthDto()));
        await Mediator.Send(new CloseMonthCommand(UserId, 2026, 2, new CloseMonthDto()));

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => Mediator.Send(new ReopenMonthCommand(UserId, 2026, 1)));

        // Deshaciendo la cadena en orden sí se puede.
        await Mediator.Send(new ReopenMonthCommand(UserId, 2026, 2));
        var reopened = await Mediator.Send(new ReopenMonthCommand(UserId, 2026, 1));

        Assert.Equal(PeriodStatus.Open, reopened.Status);
    }

    [Fact]
    public async Task Cerrar_un_mes_que_no_existe_da_no_encontrado()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => Mediator.Send(new CloseMonthCommand(UserId, 2026, 1, new CloseMonthDto())));
    }

    // ----------------------------------------------------------------------
    // Ciclo de vida del asiento
    // ----------------------------------------------------------------------

    [Fact]
    public async Task Confirmar_y_deshacer_devuelve_el_asiento_a_previsto()
    {
        var (_, concept) = SeedConcept("Subscriptions", "Netflix", ConceptKind.Expense);

        var created = await Create(new CreateLedgerEntryDto
        {
            ConceptId = concept.Id,
            Direction = EntryDirection.Out,
            DueDate = new DateOnly(2030, 8, 5),
            ForecastAmount = 13.99m
        });

        var confirmed = await Mediator.Send(new ConfirmLedgerEntryCommand(
            UserId, created.Id, new ConfirmLedgerEntryDto { ActualAmount = 14.99m }));

        Assert.Equal(EntryStatus.Paid, confirmed.Status);
        Assert.Equal(14.99m, confirmed.ActualAmount);
        Assert.Equal(-1.00m, confirmed.Remaining);

        var undone = await Mediator.Send(new UnconfirmLedgerEntryCommand(UserId, created.Id));

        Assert.Equal(EntryStatus.Planned, undone.Status);
        Assert.Null(undone.ActualAmount);
    }

    [Fact]
    public async Task Mover_la_fecha_a_otro_mes_reasigna_el_asiento_de_periodo()
    {
        var (_, concept) = SeedConcept("Subscriptions", "Netflix", ConceptKind.Expense);

        var created = await Create(new CreateLedgerEntryDto
        {
            ConceptId = concept.Id,
            Direction = EntryDirection.Out,
            DueDate = new DateOnly(2030, 3, 5),
            ForecastAmount = 13.99m
        });

        await Mediator.Send(new UpdateLedgerEntryCommand(UserId, created.Id,
            new UpdateLedgerEntryDto { DueDate = new DateOnly(2030, 4, 5) }));

        Assert.Empty(Expenses(await Summary(2030, 3)));
        Assert.Single(Expenses(await Summary(2030, 4)));
    }

    [Fact]
    public async Task No_se_puede_mover_un_asiento_a_un_mes_cerrado()
    {
        var (_, concept) = SeedConcept("Subscriptions", "Netflix", ConceptKind.Expense);

        await Summary(2026, 3);
        await Mediator.Send(new CloseMonthCommand(UserId, 2026, 3, new CloseMonthDto()));

        var created = await Create(new CreateLedgerEntryDto
        {
            ConceptId = concept.Id,
            Direction = EntryDirection.Out,
            DueDate = new DateOnly(2026, 4, 5),
            ForecastAmount = 13.99m
        });

        await Assert.ThrowsAsync<BusinessRuleException>(() => Mediator.Send(
            new UpdateLedgerEntryCommand(UserId, created.Id,
                new UpdateLedgerEntryDto { DueDate = new DateOnly(2026, 3, 5) })));
    }

    [Fact]
    public async Task Un_asiento_nacido_de_una_regla_no_se_borra_se_descarta()
    {
        var (_, concept) = SeedConcept("Subscriptions", "Netflix", ConceptKind.Expense);

        Db.RecurringRules.Add(new RecurringRule
        {
            UserId = UserId,
            ConceptId = concept.Id,
            Direction = EntryDirection.Out,
            Frequency = RecurrenceFrequency.Monthly,
            DayOfMonth = 5,
            ForecastAmount = 13.99m,
            StartDate = new DateOnly(2026, 1, 1)
        });
        await Db.SaveChangesAsync();

        var entry = Expenses(await Summary(2026, 5)).Single();

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => Mediator.Send(new DeleteLedgerEntryCommand(UserId, entry.Id)));

        await Mediator.Send(new SkipLedgerEntryCommand(UserId, entry.Id, true));

        var tras = await Summary(2026, 5);

        // Descartado sigue existiendo, así que la regla no lo vuelve a crear,
        // y se sigue viendo para poder recuperarlo.
        Assert.Equal(EntryStatus.Skipped, Assert.Single(Expenses(tras)).Status);
        Assert.Equal(0m, tras.Totals.ExpenseForecast);
        Assert.Equal(1, await Db.LedgerEntries.CountAsync());
    }

    [Fact]
    public async Task Un_asiento_manual_si_se_puede_borrar()
    {
        var (_, concept) = SeedConcept("Subscriptions", "Netflix", ConceptKind.Expense);

        var created = await Create(new CreateLedgerEntryDto
        {
            ConceptId = concept.Id,
            Direction = EntryDirection.Out,
            DueDate = new DateOnly(2030, 8, 5),
            ForecastAmount = 13.99m
        });

        await Mediator.Send(new DeleteLedgerEntryCommand(UserId, created.Id));

        Assert.Equal(0, await Db.LedgerEntries.CountAsync());
    }

    // ----------------------------------------------------------------------
    // Aislamiento entre usuarios
    // ----------------------------------------------------------------------

    [Fact]
    public async Task Un_usuario_no_ve_ni_toca_los_asientos_de_otro()
    {
        var (_, concept) = SeedConcept("Subscriptions", "Netflix", ConceptKind.Expense);

        var mine = await Create(new CreateLedgerEntryDto
        {
            ConceptId = concept.Id,
            Direction = EntryDirection.Out,
            DueDate = new DateOnly(2030, 7, 5),
            ForecastAmount = 13.99m
        });

        var intruder = Guid.NewGuid();

        await Assert.ThrowsAsync<NotFoundException>(() => Mediator.Send(
            new ConfirmLedgerEntryCommand(intruder, mine.Id, new ConfirmLedgerEntryDto { ActualAmount = 999m })));

        await Assert.ThrowsAsync<NotFoundException>(
            () => Mediator.Send(new SkipLedgerEntryCommand(intruder, mine.Id, true)));

        await Assert.ThrowsAsync<NotFoundException>(
            () => Mediator.Send(new DeleteLedgerEntryCommand(intruder, mine.Id)));

        var entry = Expenses(await Summary(2030, 7)).Single();

        Assert.Equal(EntryStatus.Planned, entry.Status);
        Assert.Null(entry.ActualAmount);
        Assert.Empty(Expenses(await Summary(2030, 7, intruder)));
    }

    [Fact]
    public async Task No_se_puede_usar_un_concepto_de_otro_usuario()
    {
        var (_, concept) = SeedConcept("Subscriptions", "Netflix", ConceptKind.Expense);

        var dto = new CreateLedgerEntryDto
        {
            ConceptId = concept.Id,
            Direction = EntryDirection.Out,
            DueDate = new DateOnly(2030, 7, 5),
            ForecastAmount = 13.99m
        };

        await Assert.ThrowsAsync<BusinessRuleException>(() => Create(dto, Guid.NewGuid()));
    }
}
