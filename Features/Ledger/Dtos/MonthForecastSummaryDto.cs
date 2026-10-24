using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Dtos
{
    /// <summary>
    /// A month as the forecast sees it: the same numbers as the dashboard boxes,
    /// the expense plan and the AI.
    /// </summary>
    public class MonthForecastSummaryDto
    {
        public int Year { get; set; }
        public int Month { get; set; }

        /// <summary>Everything expected to come in.</summary>
        public decimal Income { get; set; }

        /// <summary>Everything expected to go out.</summary>
        public decimal Expense { get; set; }

        /// <summary>Income − Expense: what is left when the month ends.</summary>
        public decimal Balance { get; set; }

        /// <summary>Entries already written in the month (paid or not).</summary>
        public decimal EntryIncome { get; set; }
        public decimal EntryExpense { get; set; }

        /// <summary>Recurring payments of the month that are not written as entries yet.</summary>
        public decimal RuleIncome { get; set; }
        public decimal RuleExpense { get; set; }

        /// <summary>What is still left of the variable expense limits.</summary>
        public decimal VariableExpenseReserve { get; set; }

        /// <summary>Variable income still expected up to its estimate.</summary>
        public decimal VariableIncomeExpected { get; set; }

        /// <summary>Pending reminders with an amount.</summary>
        public decimal Reminders { get; set; }

        /// <summary>What open settlements still expect to collect / to pay.</summary>
        public decimal SettlementIncome { get; set; }
        public decimal SettlementExpense { get; set; }

        public List<MonthForecastReserveDto> ReserveItems { get; set; } = new();
        public List<MonthForecastReminderDto> ReminderItems { get; set; } = new();
        public List<MonthForecastSettlementDto> SettlementItems { get; set; } = new();

        public static MonthForecastSummaryDto From(MonthForecast forecast) => new()
        {
            Year = forecast.Year,
            Month = forecast.Month,
            Income = forecast.Income,
            Expense = forecast.Expense,
            Balance = forecast.Balance,
            EntryIncome = forecast.Items
                .Where(item => item.Kind == ForecastItemKind.Entry && item.Direction == EntryDirection.In)
                .Sum(item => item.Amount),
            EntryExpense = forecast.Items
                .Where(item => item.Kind == ForecastItemKind.Entry && item.Direction == EntryDirection.Out)
                .Sum(item => item.Amount),
            RuleIncome = forecast.RuleIncome,
            RuleExpense = forecast.RuleExpense,
            VariableExpenseReserve = forecast.VariableExpenseReserve,
            VariableIncomeExpected = forecast.VariableIncomeExpected,
            Reminders = forecast.Reminders,
            SettlementIncome = forecast.SettlementIncome,
            SettlementExpense = forecast.SettlementExpense,
            ReserveItems = forecast.Items
                .Where(item => item.Kind == ForecastItemKind.VariableReserve)
                .Select(item => new MonthForecastReserveDto
                {
                    ConceptId = item.ConceptId ?? Guid.Empty,
                    ConceptName = item.ConceptName ?? item.Description,
                    IsIncome = item.Direction == EntryDirection.In,
                    Limit = item.Limit ?? 0m,
                    Used = item.Used ?? 0m,
                    Amount = item.Amount
                })
                .ToList(),
            ReminderItems = forecast.Items
                .Where(item => item.Kind == ForecastItemKind.Reminder)
                .Select(item => new MonthForecastReminderDto
                {
                    ReminderId = item.ReminderId ?? Guid.Empty,
                    Title = item.Description,
                    Date = item.Date,
                    Amount = item.Amount,
                    IsOverdue = item.IsOverdue
                })
                .ToList(),
            SettlementItems = forecast.Items
                .Where(item => item.Kind == ForecastItemKind.Settlement)
                .Select(item => new MonthForecastSettlementDto
                {
                    SettlementId = item.SettlementId ?? Guid.Empty,
                    Title = item.Description,
                    Date = item.Date,
                    IsIncome = item.Direction == EntryDirection.In,
                    Amount = item.Amount,
                    IsOverdue = item.IsOverdue
                })
                .ToList()
        };
    }

    public class MonthForecastReserveDto
    {
        public Guid ConceptId { get; set; }
        public string ConceptName { get; set; } = string.Empty;
        public bool IsIncome { get; set; }
        public decimal Limit { get; set; }
        public decimal Used { get; set; }

        /// <summary>Limit − used: what the forecast still keeps for this concept.</summary>
        public decimal Amount { get; set; }
    }

    public class MonthForecastReminderDto
    {
        public Guid ReminderId { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public decimal Amount { get; set; }

        /// <summary>Due before this month and not completed yet: counted on the 1st.</summary>
        public bool IsOverdue { get; set; }
    }

    public class MonthForecastSettlementDto
    {
        public Guid SettlementId { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateOnly Date { get; set; }

        /// <summary>True: she pays me. False: I pay her.</summary>
        public bool IsIncome { get; set; }

        public decimal Amount { get; set; }

        /// <summary>Its expected date already passed and it is still open.</summary>
        public bool IsOverdue { get; set; }
    }
}