using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Tests;

public sealed class MonthOutlookCalculatorTests
{
    private static readonly DateOnly Opening = new(2026, 10, 1);

    private readonly Account _personal = new() { Name = "Revolut 6931", OpeningBalance = 18.42m, OpeningDate = Opening };
    private readonly Account _joint = new() { Name = "Conjunta", OpeningBalance = 14.26m, OpeningDate = Opening };
    private readonly Concept _salary = new() { Name = "Nómina", Kind = ConceptKind.Income, Nature = ConceptNature.Fixed };
    private readonly Concept _rent = new() { Name = "Alquiler", Kind = ConceptKind.Expense, Nature = ConceptNature.Fixed };
    private readonly Concept _groceries = new()
    {
        Name = "Supermercado",
        Kind = ConceptKind.Expense,
        Nature = ConceptNature.Variable,
        DefaultMonthlyBudget = 300m
    };

    [Fact]
    public void Proyecta_cada_cuenta_con_reglas_no_materializadas_y_sugiere_la_transferencia()
    {
        var outlook = Build(
            today: new DateOnly(2026, 9, 28),
            rules: new[]
            {
                Rule(_salary, _personal, EntryDirection.In, 2360m, 29),
                Rule(_rent, _joint, EntryDirection.Out, 737.97m, 7)
            });

        var personal = outlook.Accounts.Single(account => account.AccountId == _personal.Id);
        var joint = outlook.Accounts.Single(account => account.AccountId == _joint.Id);

        Assert.Equal(18.42m, personal.BaseBalance);
        Assert.Equal(2378.42m, personal.ProjectedEndBalance);
        Assert.Equal(18.42m, personal.LowestBalance);
        Assert.Equal(14.26m - 737.97m, joint.ProjectedEndBalance);
        Assert.Equal(new DateOnly(2026, 10, 7), joint.LowestBalanceDate);
        Assert.Equal(723.71m, joint.Shortfall);

        // La nómina entra el 29 y el alquiler sale el 7: Revolut solo puede aportar lo que tiene antes.
        var transfer = Assert.Single(outlook.SuggestedTransfers);
        Assert.Equal(_personal.Id, transfer.FromAccountId);
        Assert.Equal(_joint.Id, transfer.ToAccountId);
        Assert.Equal(18.42m, transfer.Amount);
        Assert.Equal(705.29m, outlook.UncoveredShortfall);
        Assert.Equal(2, outlook.PendingItems.Count);
    }

    [Fact]
    public void No_duplica_una_regla_ya_convertida_en_asiento()
    {
        var rule = Rule(_rent, _joint, EntryDirection.Out, 737.97m, 7);
        var entry = Entry(_rent, _joint, EntryDirection.Out, EntryStatus.Planned, 737.97m, new DateOnly(2026, 10, 7));
        entry.RecurringRuleId = rule.Id;

        var outlook = Build(new DateOnly(2026, 10, 2), rules: new[] { rule }, entries: new[] { entry });

        Assert.Single(outlook.PendingItems);
        Assert.Equal(737.97m, outlook.Accounts.Single(a => a.AccountId == _joint.Id).PendingExpense);
    }

    [Fact]
    public void Marca_como_vencido_lo_que_debio_pagarse_antes_de_hoy()
    {
        var entry = Entry(_rent, _joint, EntryDirection.Out, EntryStatus.Pending, 737.97m, new DateOnly(2026, 10, 7));

        var outlook = Build(new DateOnly(2026, 10, 10), entries: new[] { entry });

        var overdue = Assert.Single(outlook.OverdueItems);
        Assert.True(overdue.IsOverdue);
        Assert.Equal("Conjunta", overdue.AccountName);
    }

    [Fact]
    public void Ignora_pendientes_anteriores_al_inicio_contable()
    {
        var september = Entry(_rent, _joint, EntryDirection.Out, EntryStatus.Pending, 500m, new DateOnly(2026, 9, 7));
        var unassigned = Entry(_rent, null, EntryDirection.Out, EntryStatus.Pending, 80m, new DateOnly(2026, 9, 12));

        var outlook = Build(new DateOnly(2026, 9, 28), entries: new[] { september, unassigned });

        Assert.Empty(outlook.OverdueItems);
        Assert.Equal(14.26m, outlook.Accounts.Single(a => a.AccountId == _joint.Id).BaseBalance);
    }

    [Fact]
    public void Reserva_lo_que_queda_del_limite_variable_y_calcula_el_desvio()
    {
        var spent = Entry(_groceries, _personal, EntryDirection.Out, EntryStatus.Paid, 0m, new DateOnly(2026, 10, 3));
        spent.ActualAmount = 120m;
        spent.ValueDate = new DateOnly(2026, 10, 3);

        var outlook = Build(new DateOnly(2026, 10, 5), entries: new[] { spent });

        Assert.Equal(180m, outlook.Unassigned.VariableExpenseReserve);
        var deviation = outlook.Deviations.Single(item => item.ConceptId == _groceries.Id);
        Assert.Equal(300m, deviation.Planned);
        Assert.Equal(120m, deviation.Actual);
        Assert.Equal(-180m, deviation.Deviation);

        // 18,42 − 120 + 14,26 − 180 de reserva.
        Assert.Equal(18.42m - 120m + 14.26m - 180m, outlook.FreeMoney);
    }

    [Fact]
    public void Reserva_asignada_a_cuenta_reduce_el_saldo_y_no_duplica_el_dinero_libre()
    {
        _groceries.AccountId = _personal.Id;
        var spent = Entry(_groceries, _personal, EntryDirection.Out, EntryStatus.Paid, 0m, new DateOnly(2026, 10, 3));
        spent.ActualAmount = 120m;
        spent.ValueDate = new DateOnly(2026, 10, 3);

        var outlook = Build(new DateOnly(2026, 10, 5), entries: new[] { spent });
        var personal = outlook.Accounts.Single(account => account.AccountId == _personal.Id);

        Assert.Equal(180m, personal.PendingExpense);
        Assert.Equal(18.42m - 120m - 180m, personal.ProjectedEndBalance);
        Assert.Equal(180m, outlook.VariableExpenseReserve);
        Assert.Equal(0m, outlook.Unassigned.VariableExpenseReserve);
        Assert.Equal(personal.ProjectedEndBalance + 14.26m, outlook.FreeMoney);
        Assert.True(Assert.Single(outlook.PendingItems).IsVariableReserve);
        Assert.Equal(0m, outlook.Deviations.Single(item => item.ConceptId == _groceries.Id).Pending);
    }

    [Fact]
    public void Reserva_de_mes_anterior_se_arrastra_al_mes_seleccionado()
    {
        _groceries.AccountId = _personal.Id;

        var outlook = Build(
            new DateOnly(2026, 10, 5),
            year: 2026,
            month: 11);
        var personal = outlook.Accounts.Single(account => account.AccountId == _personal.Id);

        Assert.Equal(18.42m - 300m, personal.BaseBalance);
        Assert.Equal(300m, personal.PendingExpense);
        Assert.Equal(18.42m - 600m, personal.ProjectedEndBalance);
        Assert.Equal(300m, outlook.VariableExpenseReserve);
        Assert.Single(outlook.PendingItems);
    }

    [Fact]
    public void Puede_excluir_la_reserva_variable_de_la_prevision()
    {
        _groceries.AccountId = _personal.Id;

        var outlook = Build(
            new DateOnly(2026, 10, 5),
            includeVariableReserve: false);

        Assert.Equal(18.42m, outlook.Accounts.Single(account => account.AccountId == _personal.Id).ProjectedEndBalance);
        Assert.Equal(0m, outlook.VariableExpenseReserve);
        Assert.Empty(outlook.PendingItems);
    }

    [Fact]
    public void Usa_el_presupuesto_de_cada_mes_para_el_arrastre()
    {
        _groceries.AccountId = _personal.Id;
        var budgets = new[]
        {
            new MonthlyBudget { ConceptId = _groceries.Id, Year = 2026, Month = 10, LimitAmount = 500m },
            new MonthlyBudget { ConceptId = _groceries.Id, Year = 2026, Month = 11, LimitAmount = 700m }
        };

        var outlook = Build(
            new DateOnly(2026, 10, 5),
            year: 2026,
            month: 11,
            budgets: budgets);
        var personal = outlook.Accounts.Single(account => account.AccountId == _personal.Id);

        Assert.Equal(18.42m - 500m, personal.BaseBalance);
        Assert.Equal(700m, personal.PendingExpense);
        Assert.Equal(700m, outlook.VariableExpenseReserve);
    }

    [Fact]
    public void Mes_pasado_no_genera_reservas_variables()
    {
        _groceries.AccountId = _personal.Id;

        var outlook = Build(new DateOnly(2026, 12, 15));

        Assert.True(outlook.IsPast);
        Assert.Equal(0m, outlook.VariableExpenseReserve);
        Assert.Empty(outlook.PendingItems);
    }

    [Fact]
    public void Detecta_si_el_saldo_guardado_en_el_cuadre_coincide()
    {
        var outlook = Build(
            new DateOnly(2026, 10, 5),
            reconciled: new Dictionary<Guid, decimal> { [_personal.Id] = 18.42m, [_joint.Id] = 20m });

        Assert.True(outlook.Accounts.Single(a => a.AccountId == _personal.Id).IsReconciled);
        Assert.False(outlook.Accounts.Single(a => a.AccountId == _joint.Id).IsReconciled);
    }

    [Fact]
    public void Un_mes_pasado_usa_el_saldo_real_y_no_proyecta()
    {
        var entry = Entry(_rent, _joint, EntryDirection.Out, EntryStatus.Pending, 737.97m, new DateOnly(2026, 10, 7));

        var outlook = Build(new DateOnly(2026, 11, 15), entries: new[] { entry });

        Assert.True(outlook.IsPast);
        Assert.Equal(14.26m, outlook.Accounts.Single(a => a.AccountId == _joint.Id).ProjectedEndBalance);
        Assert.Empty(outlook.SuggestedTransfers);
        Assert.Single(outlook.OverdueItems);
    }

    private Models.Dtos.Dashboard.MonthOutlookDto Build(
        DateOnly today,
        IEnumerable<RecurringRule>? rules = null,
        IEnumerable<LedgerEntry>? entries = null,
        IReadOnlyDictionary<Guid, decimal>? reconciled = null,
        int year = 2026,
        int month = 10,
        bool includeVariableReserve = true,
        IEnumerable<MonthlyBudget>? budgets = null)
    {
        var entryList = (entries ?? Array.Empty<LedgerEntry>()).ToList();
        return MonthOutlookCalculator.Build(new MonthOutlookInput(
            year,
            month,
            today,
            new[] { _personal, _joint },
            entryList.Where(entry => entry.Status == EntryStatus.Paid).ToList(),
            entryList,
            (rules ?? Array.Empty<RecurringRule>()).ToList(),
            new[] { _salary, _rent, _groceries },
            (budgets ?? Array.Empty<MonthlyBudget>()).ToList(),
            reconciled ?? new Dictionary<Guid, decimal>()),
            includeVariableReserve);
    }

    private static RecurringRule Rule(Concept concept, Account account, EntryDirection direction, decimal amount, int day) => new()
    {
        ConceptId = concept.Id,
        AccountId = account.Id,
        Direction = direction,
        ForecastAmount = amount,
        DayOfMonth = day,
        StartDate = Opening
    };

    private static LedgerEntry Entry(
        Concept concept,
        Account? account,
        EntryDirection direction,
        EntryStatus status,
        decimal forecast,
        DateOnly dueDate) => new()
    {
        ConceptId = concept.Id,
        AccountId = account?.Id,
        Direction = direction,
        Status = status,
        ForecastAmount = forecast,
        DueDate = dueDate
    };
}
