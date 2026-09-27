using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Tests;

public sealed class DashboardLedgerCalculatorTests
{
    [Fact]
    public void Suma_movimientos_reales_y_previstos_sin_contar_traspasos()
    {
        var entries = new[]
        {
            Entry(EntryDirection.In, EntryStatus.Paid, 100m, 95m),
            Entry(EntryDirection.Out, EntryStatus.Paid, 40m, 42m),
            Entry(EntryDirection.Out, EntryStatus.Pending, 15m, null),
            Entry(EntryDirection.In, EntryStatus.Planned, 20m, null),
            Entry(EntryDirection.Out, EntryStatus.Skipped, 50m, null),
            Entry(EntryDirection.Out, EntryStatus.Paid, 200m, 200m, isTransfer: true),
            Entry(EntryDirection.In, EntryStatus.Paid, 999m, 999m, month: 8)
        };

        var totals = DashboardLedgerCalculator.ForMonth(entries, 2026, 9);

        Assert.Equal(115m, totals.Income);
        Assert.Equal(57m, totals.Expense);
    }

    private static LedgerEntry Entry(
        EntryDirection direction,
        EntryStatus status,
        decimal forecast,
        decimal? actual,
        bool isTransfer = false,
        int month = 9) =>
        new()
        {
            Direction = direction,
            Status = status,
            ForecastAmount = forecast,
            ActualAmount = actual,
            IsTransfer = isTransfer,
            DueDate = new DateOnly(2026, month, 1)
        };
}
