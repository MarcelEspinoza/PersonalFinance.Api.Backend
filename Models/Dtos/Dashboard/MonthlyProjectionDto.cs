using System;

namespace PersonalFinance.Api.Models.Dtos.Dashboard
{
    /// <summary>
    /// Represents a monthly projection entry used by the dashboard.
    /// </summary>
    public class MonthlyProjectionDto
    {
        /// <summary>
        /// Localized month label (e.g., "marzo 2025").
        /// </summary>
        public string Month { get; set; } = string.Empty;
        public int Year { get; set; }
        public int MonthNumber { get; set; }

        /// <summary>
        /// Total income for the month.
        /// </summary>
        public decimal Income { get; set; }

        /// <summary>
        /// Total expense for the month.
        /// </summary>
        public decimal Expense { get; set; }

        /// <summary>
        /// Balance = Income - Expense.
        /// </summary>
        public decimal Balance { get; set; }

        /// <summary>
        /// Indicates whether this projection corresponds to the current month.
        /// </summary>
        public bool IsCurrent { get; set; }
        public string ProjectionSource { get; set; } = string.Empty;
        public decimal PendingIncome { get; set; }
        public decimal PendingExpense { get; set; }

        /// <summary>Part of Expense kept for what is left of the variable expense limits.</summary>
        public decimal VariableExpenseReserve { get; set; }

        /// <summary>Part of Expense that comes from pending reminders with an amount.</summary>
        public decimal Reminders { get; set; }

        /// <summary>Part of Income that open settlements still expect to collect.</summary>
        public decimal SettlementIncome { get; set; }

        /// <summary>Part of Expense that open settlements still expect to pay.</summary>
        public decimal SettlementExpense { get; set; }

        public CommitmentSummaryDto? Commitments { get; set; }
    }

    public class DashboardAccountBalanceDto
    {
        public Guid AccountId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Currency { get; set; } = "EUR";
        public decimal Balance { get; set; }
    }
}
