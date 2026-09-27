using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Features.Ledger.Commands.SeedChartOfAccounts;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Features.Ledger.Queries.GetChartOfAccounts;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Tests;

public class ChartOfAccountsTests : LedgerTestBase
{
    private static int ExpectedGroups => ChartOfAccountsTemplate.Groups.Count;
    private static int ExpectedConcepts => ChartOfAccountsTemplate.Groups.Sum(g => g.Concepts.Count);

    [Fact]
    public async Task La_siembra_crea_un_plan_de_cuentas_personal()
    {
        var result = await Mediator.Send(new SeedChartOfAccountsCommand(UserId));

        Assert.Equal(ExpectedGroups, result.GroupsCreated);
        Assert.Equal(ExpectedConcepts, result.ConceptsCreated);
        Assert.Equal(0, result.BudgetsCreated);

        var chart = await Mediator.Send(new GetChartOfAccountsQuery(UserId));

        Assert.Equal(ExpectedGroups, chart.Groups.Count);
        Assert.Single(chart.Groups, g => g.Kind == ConceptKind.Income);
        Assert.Contains(chart.Groups, g => g.Name == "Suscripciones y servicios digitales");
        Assert.Contains(
            chart.Groups.Single(g => g.Name == "Suscripciones y servicios digitales").Concepts,
            concept => concept.Name == "Amazon Prime");
        Assert.Contains(
            chart.Groups.Single(g => g.Name == "Suscripciones y servicios digitales").Concepts,
            concept => concept.Name == "Apple");
        Assert.Contains(
            chart.Groups.Single(g => g.Name == "Suscripciones y servicios digitales").Concepts,
            concept => concept.Name == "Spliiit");
        Assert.Contains(
            chart.Groups.Single(g => g.Name == "Vida personal").Concepts,
            concept => concept.Name == "Mascotas (Zoey y Zeus)");
        Assert.Contains(
            chart.Groups.Single(g => g.Name == "Ingresos").Concepts,
            concept => concept.Name == "Reembolsos de Vanessa");
        Assert.Contains(
            chart.Groups.Single(g => g.Name == "Ingresos").Concepts,
            concept => concept.Name == "Liquidaciones recibidas de mamá");
        Assert.Contains(
            chart.Groups.Single(g => g.Name == "Finanzas y compromisos").Concepts,
            concept => concept.Name == "Liquidaciones pagadas a mamá");
    }

    [Fact]
    public async Task Repetir_la_siembra_no_duplica_nada()
    {
        await Mediator.Send(new SeedChartOfAccountsCommand(UserId));
        var second = await Mediator.Send(new SeedChartOfAccountsCommand(UserId));

        Assert.Equal(0, second.GroupsCreated);
        Assert.Equal(0, second.ConceptsCreated);
        Assert.Equal(ExpectedGroups, second.GroupsAlreadyPresent);
        Assert.Equal(ExpectedConcepts, second.ConceptsAlreadyPresent);

        Assert.Equal(ExpectedGroups, await Db.ConceptGroups.CountAsync());
        Assert.Equal(ExpectedConcepts, await Db.Concepts.CountAsync());
    }

    [Fact]
    public async Task La_siembra_oculta_la_plantilla_antigua_sin_borrar_el_historico()
    {
        var legacyGroup = new ConceptGroup
        {
            UserId = UserId,
            Name = "Variable Costs",
            Kind = ConceptKind.Expense,
            SortOrder = 50
        };
        var legacyConcept = new Concept
        {
            UserId = UserId,
            Group = legacyGroup,
            Name = "Food & Beverage",
            Kind = ConceptKind.Expense,
            Nature = ConceptNature.Variable,
            SortOrder = 10
        };
        var period = new MonthlyPeriod { UserId = UserId, Year = 2026, Month = 1 };
        var entry = new LedgerEntry
        {
            UserId = UserId,
            Period = period,
            Concept = legacyConcept,
            Direction = EntryDirection.Out,
            Status = EntryStatus.Paid,
            DueDate = new DateOnly(2026, 1, 10),
            ForecastAmount = 25m,
            ActualAmount = 25m
        };
        Db.ConceptGroups.Add(legacyGroup);
        Db.Concepts.Add(legacyConcept);
        Db.MonthlyPeriods.Add(period);
        Db.LedgerEntries.Add(entry);
        await Db.SaveChangesAsync();

        await Mediator.Send(new SeedChartOfAccountsCommand(UserId));

        Assert.False((await Db.ConceptGroups.FindAsync(legacyGroup.Id))!.IsActive);
        Assert.False((await Db.Concepts.FindAsync(legacyConcept.Id))!.IsActive);
        Assert.True(await Db.ConceptGroups.AnyAsync(
            group => group.UserId == UserId && group.Name == "Finanzas y compromisos" && group.IsActive));
        var migratedEntry = await Db.LedgerEntries
            .AsNoTracking()
            .Include(item => item.Concept)
            .SingleAsync(item => item.Id == entry.Id);
        Assert.Equal("Supermercado y alimentación del hogar", migratedEntry.Concept!.Name);
    }

    [Fact]
    public async Task Los_conceptos_heredan_el_tipo_de_su_grupo()
    {
        await Mediator.Send(new SeedChartOfAccountsCommand(UserId));

        var chart = await Mediator.Send(new GetChartOfAccountsQuery(UserId));

        foreach (var group in chart.Groups)
        {
            var conceptIds = group.Concepts.Select(c => c.Id).ToList();
            var kinds = await Db.Concepts
                .Where(c => conceptIds.Contains(c.Id))
                .Select(c => c.Kind)
                .Distinct()
                .ToListAsync();

            Assert.Equal(new[] { group.Kind }, kinds);
        }
    }

    [Fact]
    public async Task Con_ano_y_mes_tambien_siembra_los_presupuestos_variables()
    {
        var result = await Mediator.Send(new SeedChartOfAccountsCommand(UserId, 2026, 2));

        var withBudget = ChartOfAccountsTemplate.Groups
            .SelectMany(g => g.Concepts)
            .Count(c => c.DefaultMonthlyBudget.HasValue);

        Assert.Equal(withBudget, result.BudgetsCreated);

        var food = await Db.Concepts.SingleAsync(c => c.Name == "Supermercado y alimentación del hogar");
        var budget = await Db.MonthlyBudgets.SingleAsync(
            b => b.ConceptId == food.Id && b.Year == 2026 && b.Month == 2);

        Assert.Equal(300m, budget.LimitAmount);
    }

    [Fact]
    public async Task Repetir_la_siembra_con_presupuesto_no_duplica_el_presupuesto()
    {
        await Mediator.Send(new SeedChartOfAccountsCommand(UserId, 2026, 2));
        var second = await Mediator.Send(new SeedChartOfAccountsCommand(UserId, 2026, 2));

        Assert.Equal(0, second.BudgetsCreated);
    }

    [Fact]
    public async Task El_plan_de_cuentas_de_un_usuario_no_se_ve_desde_otro()
    {
        await Mediator.Send(new SeedChartOfAccountsCommand(UserId));

        var chart = await Mediator.Send(new GetChartOfAccountsQuery(Guid.NewGuid()));

        Assert.Empty(chart.Groups);
    }

    [Fact]
    public async Task El_plan_sembrado_sirve_para_crear_asientos_del_mes()
    {
        await Mediator.Send(new SeedChartOfAccountsCommand(UserId));

        var chart = await Mediator.Send(new GetChartOfAccountsQuery(UserId));
        var salary = chart.Groups.Single(g => g.Name == "Ingresos").Concepts.Single(c => c.Name == "Nómina");

        await Mediator.Send(new Features.Ledger.Commands.CreateLedgerEntry.CreateLedgerEntryCommand(
            UserId, new Features.Ledger.Dtos.CreateLedgerEntryDto
            {
                ConceptId = salary.Id,
                Direction = EntryDirection.In,
                DueDate = new DateOnly(2026, 1, 25),
                ForecastAmount = 2000m
            }));

        var summary = await Mediator.Send(
            new Features.Ledger.Queries.GetMonthlySummary.GetMonthlySummaryQuery(UserId, 2026, 1));

        var group = Assert.Single(summary.IncomeGroups);

        Assert.Equal("Ingresos", group.Name);
        Assert.Equal(2000m, group.ForecastTotal);

        // Los grupos de gasto salen aunque el mes aún no tenga movimiento:
        // son las filas en blanco donde anotar.
        Assert.NotEmpty(summary.ExpenseGroups);
        Assert.All(summary.ExpenseGroups, g => Assert.Equal(0m, g.ForecastTotal));
        Assert.Contains(summary.ExpenseGroups.SelectMany(g => g.Concepts), c => c.Name == "Netflix");
    }
}
