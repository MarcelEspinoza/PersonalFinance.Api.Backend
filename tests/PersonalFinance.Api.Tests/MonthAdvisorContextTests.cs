using System.Text.Json;
using PersonalFinance.Api.Features.Chat;
using PersonalFinance.Api.Models.Dtos.Dashboard;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Tests;

public sealed class MonthAdvisorContextTests
{
    [Fact]
    public void Timeline_accumulates_balance_with_income_before_expense_on_same_day()
    {
        var accountId = Guid.NewGuid();
        var outlook = new MonthOutlookDto
        {
            Year = 2026,
            Month = 10,
            Accounts = { new AccountOutlookDto { AccountId = accountId, Name = "Revolut", BaseBalance = 18.42m } },
            PendingItems =
            {
                Item(accountId, 29, EntryDirection.In, 2360m, "Nómina"),
                Item(accountId, 5, EntryDirection.Out, 737.97m, "Alquiler"),
                Item(accountId, 1, EntryDirection.Out, 118.50m, "Mamá"),
                Item(accountId, 1, EntryDirection.In, 450m, "Casa mamá")
            }
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(MonthAdvisorService.BuildMonthContext(outlook)));
        var timeline = json.RootElement.GetProperty("accounts")[0].GetProperty("timeline");

        Assert.Equal("Casa mamá", timeline[0].GetProperty("concept").GetString());
        Assert.Equal(468.42m, timeline[0].GetProperty("balanceAfter").GetDecimal());
        Assert.Equal(349.92m, timeline[1].GetProperty("balanceAfter").GetDecimal());
        Assert.Equal(-388.05m, timeline[2].GetProperty("balanceAfter").GetDecimal());
        Assert.Equal(1971.95m, timeline[3].GetProperty("balanceAfter").GetDecimal());
    }

    [Fact]
    public void Account_without_items_is_flagged()
    {
        var outlook = new MonthOutlookDto
        {
            Year = 2026,
            Month = 10,
            Accounts = { new AccountOutlookDto { AccountId = Guid.NewGuid(), Name = "Conjunta" } }
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(MonthAdvisorService.BuildMonthContext(outlook)));

        Assert.True(json.RootElement.GetProperty("accounts")[0].GetProperty("hasNoMovements").GetBoolean());
    }

    private static OutlookItemDto Item(Guid accountId, int day, EntryDirection direction, decimal amount, string description) =>
        new()
        {
            AccountId = accountId,
            DueDate = new DateOnly(2026, 10, day),
            Direction = direction,
            Amount = amount,
            Description = description
        };
}
