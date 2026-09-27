using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Controllers;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Tests;

public sealed class ExpensePlanningTests : LedgerTestBase
{
    [Fact]
    public async Task Cambiar_de_fijo_a_variable_desactiva_la_regla_y_guarda_el_limite()
    {
        var (_, concept) = SeedConcept("Hogar", "Electricidad", ConceptKind.Expense);
        var controller = CreateController();

        var fixedResult = await controller.Update(
            concept.Id,
            new UpdateExpensePlanningDto
            {
                Nature = ConceptNature.Fixed,
                MonthlyAmount = 85m,
                DayOfMonth = 12
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
