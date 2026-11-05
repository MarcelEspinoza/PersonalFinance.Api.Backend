using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Controllers;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Api.Features.Ledger.Queries.GetMonthlySummary;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Tests;

public sealed class ExpensePlanningTests : LedgerTestBase
{
    [Fact]
    public async Task Cambiar_de_fijo_a_variable_desactiva_la_regla_y_guarda_el_limite()
    {
        var (_, concept) = SeedConcept("Hogar", "Electricidad", ConceptKind.Expense);
        var account = SeedAccount();
        var controller = CreateController();

        var fixedResult = await controller.Update(
            concept.Id,
            new UpdateExpensePlanningDto
            {
                Nature = ConceptNature.Fixed,
                MonthlyAmount = 85m,
                DayOfMonth = 12,
                AccountId = account.Id
            },
            default);

        Assert.IsType<OkObjectResult>(fixedResult.Result);
        var rule = await Db.RecurringRules.SingleAsync();
        Assert.True(rule.IsActive);
        Assert.Equal(85m, rule.ForecastAmount);
        Assert.Equal(12, rule.DayOfMonth);

        var variableResult = await controller.Update(
            concept.Id,
            new UpdateExpensePlanningDto
            {
                Nature = ConceptNature.Variable,
                MonthlyBudget = 120m
            },
            default);

        Assert.IsType<OkObjectResult>(variableResult.Result);
        Assert.False((await Db.RecurringRules.SingleAsync()).IsActive);
        var savedConcept = await Db.Concepts.SingleAsync(item => item.Id == concept.Id);
        Assert.Equal(ConceptNature.Variable, savedConcept.Nature);
        Assert.Equal(120m, savedConcept.DefaultMonthlyBudget);
    }

    [Fact]
    public async Task Configurar_un_ingreso_fijo_crea_una_regla_de_entrada()
    {
        var (_, concept) = SeedConcept("Ingresos", "Nómina", ConceptKind.Income);
        var account = SeedAccount();
        var controller = CreateController();

        var result = await controller.Update(
            concept.Id,
            new UpdateExpensePlanningDto
            {
                Nature = ConceptNature.Fixed,
                MonthlyAmount = 2360m,
                DayOfMonth = 29,
                AccountId = account.Id
            },
            default);

        Assert.IsType<OkObjectResult>(result.Result);
        var rule = await Db.RecurringRules.SingleAsync();
        Assert.Equal(EntryDirection.In, rule.Direction);
        Assert.Equal(2360m, rule.ForecastAmount);
        Assert.Equal(29, rule.DayOfMonth);
        Assert.Equal(account.Id, rule.AccountId);
    }

    [Fact]
    public async Task La_planificacion_devuelve_ingresos_y_gastos()
    {
        SeedConcept("Ingresos", "Nómina", ConceptKind.Income);
        SeedConcept("Hogar", "Alquiler", ConceptKind.Expense);

        var result = await CreateController().Get(default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var planning = Assert.IsType<ExpensePlanningDto>(ok.Value);
        Assert.Contains(planning.Groups, group => group.Kind == ConceptKind.Income);
        Assert.Contains(planning.Groups, group => group.Kind == ConceptKind.Expense);
    }

    [Fact]
    public async Task Leer_la_planificacion_no_cambia_cuentas_ni_reglas()
    {
        var (_, rent) = SeedConcept("Hogar", "Alquiler", ConceptKind.Expense);
        var account = new Account
        {
            UserId = UserId,
            Name = "Conjunta 1646",
            OpeningDate = new DateOnly(2026, 1, 1)
        };
        Db.Accounts.Add(account);
        Db.ImportBatches.Add(new ImportBatch
        {
            UserId = UserId,
            AccountId = account.Id,
            FileName = "account-statement_2026-03-01_2026-09-25_es-es_fad8bc.csv"
        });
        Db.RecurringRules.Add(new RecurringRule
        {
            UserId = UserId,
            ConceptId = rent.Id,
            ForecastAmount = 0m,
            StartDate = new DateOnly(2026, 10, 1)
        });
        await Db.SaveChangesAsync();

        var result = await CreateController().Get(default);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Conjunta 1646", account.Name);
        var rule = await Db.RecurringRules.SingleAsync();
        Assert.Equal(0m, rule.ForecastAmount);
        Assert.Null(rule.AccountId);
    }

    [Fact]
    public async Task Un_fijo_cada_dos_meses_quita_el_recibo_de_los_meses_que_no_tocan()
    {
        var (_, concept) = SeedConcept("Hogar", "Agua", ConceptKind.Expense);
        var account = SeedAccount();
        var controller = CreateController();

        await controller.Update(
            concept.Id,
            new UpdateExpensePlanningDto
            {
                Nature = ConceptNature.Fixed,
                MonthlyAmount = 75m,
                DayOfMonth = 5,
                AccountId = account.Id
            },
            default);
        await Mediator.Send(new GetMonthlySummaryQuery(UserId, 2026, 1));
        await Mediator.Send(new GetMonthlySummaryQuery(UserId, 2026, 2));
        Assert.Equal(2, await Db.LedgerEntries.CountAsync(entry => entry.ConceptId == concept.Id));

        var result = await controller.Update(
            concept.Id,
            new UpdateExpensePlanningDto
            {
                Nature = ConceptNature.Fixed,
                MonthlyAmount = 75m,
                DayOfMonth = 5,
                AccountId = account.Id,
                Frequency = RecurrenceFrequency.Bimonthly,
                FirstDueMonth = new DateOnly(2026, 2, 1)
            },
            default);

        var item = Assert.IsType<ExpensePlanningItemDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(RecurrenceFrequency.Bimonthly, item.Frequency);
        Assert.Equal(new DateOnly(2026, 2, 1), item.NextDueMonth);
        var remaining = Assert.Single(await Db.LedgerEntries
            .Where(entry => entry.ConceptId == concept.Id)
            .ToListAsync());
        Assert.Equal(new DateOnly(2026, 2, 5), remaining.DueDate);

        await Mediator.Send(new GetMonthlySummaryQuery(UserId, 2026, 3));
        await Mediator.Send(new GetMonthlySummaryQuery(UserId, 2026, 4));
        var dueDates = (await Db.LedgerEntries
                .Where(entry => entry.ConceptId == concept.Id)
                .Select(entry => entry.DueDate)
                .ToListAsync())
            .OrderBy(date => date)
            .ToList();
        Assert.Equal(new[] { new DateOnly(2026, 2, 5), new DateOnly(2026, 4, 5) }, dueDates);

        var resaved = await controller.Update(
            concept.Id,
            new UpdateExpensePlanningDto
            {
                Nature = ConceptNature.Fixed,
                MonthlyAmount = 80m,
                DayOfMonth = 5,
                AccountId = account.Id
            },
            default);

        var resavedItem = Assert.IsType<ExpensePlanningItemDto>(Assert.IsType<OkObjectResult>(resaved.Result).Value);
        Assert.Equal(RecurrenceFrequency.Bimonthly, resavedItem.Frequency);
        Assert.Equal(new DateOnly(2026, 2, 1), resavedItem.NextDueMonth);
    }

    private Account SeedAccount()
    {
        var account = new Account
        {
            UserId = UserId,
            Name = "Revolut",
            OpeningDate = new DateOnly(2026, 1, 1)
        };
        Db.Accounts.Add(account);
        Db.SaveChanges();
        return account;
    }

    private ExpensePlanningController CreateController()
    {
        var controller = new ExpensePlanningController(Db, Clock);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, UserId.ToString()) },
                    "Test"))
            }
        };
        return controller;
    }
}
