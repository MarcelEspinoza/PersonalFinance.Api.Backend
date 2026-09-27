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
        public decimal ClosingBalance { get; set; }

        /// <summary>
        /// Indicates whether this projection corresponds to the current month.
        /// </summary>
        public bool IsCurrent { get; set; }
        public bool IsEstimate { get; set; }
        public string ProjectionSource { get; set; } = string.Empty;
        public decimal PendingIncome { get; set; }
        public decimal PendingExpense { get; set; }

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
