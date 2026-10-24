using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Tests;

public sealed class ExpensePlanCalculatorTests
{
    private static readonly DateOnly October = new(2026, 10, 1);
    private static readonly DateOnly November = new(2026, 11, 1);

    private readonly ConceptGroup _home = new() { Name = "Casa", Kind = ConceptKind.Expense, SortOrder = 1 };
    private readonly Concept _salary = new() { Name = "Nómina", Kind = ConceptKind.Income, Nature = ConceptNature.Fixed };
    private readonly Concept _rent;
    private readonly Concept _groceries;

    public ExpensePlanCalculatorTests()
    {
        _rent = new Concept
        {
            Name = "Alquiler",
            Kind = ConceptKind.Expense,
            Nature = ConceptNature.Fixed,
            GroupId = _home.Id
        };
        _groceries = new Concept
        {
            Name = "Supermercado",
            Kind = ConceptKind.Expense,
            Nature = ConceptNature.Variable,
            GroupId = _home.Id,
            DefaultMonthlyBudget = 300m
        };
    }

    [Fact]
    public void Separa_fijos_y_variables_con_los_mismos_totales_que_la_prevision()
    {
        var rentRule = Rule(_rent, EntryDirection.Out, 700m, day: 1);
        var salaryRule = Rule(_salary, EntryDirection.In, 2000m, day: 28);
        var entries = new[]
        {
            new LedgerEntry
            {
                ConceptId = _rent.Id,
                RecurringRuleId = rentRule.Id,
                Direction = EntryDirection.Out,
                ForecastAmount = 700m,
                ActualAmount = 700m,
                Status = EntryStatus.Paid,
                DueDate = new DateOnly(2026, 10, 1)
            },
            new LedgerEntry
            {
                ConceptId = _groceries.Id,
                Direction = EntryDirection.Out,
                ForecastAmount = 120m,
                ActualAmount = 120m,
                Status = EntryStatus.Paid,
                DueDate = new DateOnly(2026, 10, 5)
            },
            new LedgerEntry
            {
                ConceptId = _groceries.Id,
                Direction = EntryDirection.Out,
                ForecastAmount = 350m,
                Status = EntryStatus.Planned,
                DueDate = new DateOnly(2026, 11, 5)
            }
        };
        var budgets = new[]
        {
            new MonthlyBudget { ConceptId = _groceries.Id, Year = 2026, Month = 10, LimitAmount = 250m }
        };

        var plan = ExpensePlanCalculator.Build(
            new[] { October, November },
            October,
            entries,
            new[] { rentRule, salaryRule },
            new[] { _salary, _rent, _groceries },
            new[] { _home },
            budgets,
            new HashSet<DateOnly>());

        Assert.True(plan.Months[0].IsCurrent);
        Assert.Equal(2000m, plan.Months[0].Income);
        Assert.Equal(700m, plan.Months[0].FixedExpense);
        Assert.Equal(250m, plan.Months[0].VariableExpense);
        Assert.Equal(700m, plan.Months[1].FixedExpense);
        Assert.Equal(350m, plan.Months[1].VariableExpense);

        var rent = Assert.Single(plan.Fixed);
        Assert.Equal("Casa", rent.GroupName);
        Assert.True(rent.Cells[0].Paid);
        Assert.False(rent.Cells[1].Paid);

        var groceries = Assert.Single(plan.Variable);
        Assert.Equal(300m, groceries.DefaultBudget);
        Assert.Equal(250m, groceries.Cells[0].Budget);
        Assert.True(groceries.Cells[0].IsOverride);
        Assert.Equal(120m, groceries.Cells[0].Spent);
        Assert.Equal(300m, groceries.Cells[1].Budget);
        Assert.False(groceries.Cells[1].IsOverride);
        Assert.Equal(350m, groceries.Cells[1].Counted);
    }

    private static RecurringRule Rule(Concept concept, EntryDirection direction, decimal amount, int day) => new()
    {
        ConceptId = concept.Id,
        Direction = direction,
        ForecastAmount = amount,
        DayOfMonth = day,
        StartDate = October
    };
}
