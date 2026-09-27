using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Controllers;
using PersonalFinance.Api.Features.Ledger.Dtos;
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
