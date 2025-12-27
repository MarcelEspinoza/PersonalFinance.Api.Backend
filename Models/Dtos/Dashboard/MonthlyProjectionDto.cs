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

        public decimal Savings { get; set; }

        // Ahorro proyectado para meses futuros (y opcionalmente para el actual si quieres un objetivo)
        public decimal ProjectedSavings { get; set; }
        public decimal? PlannedBalance { get; set; }

        public CommitmentSummaryDto? Commitments { get; set; }

    }
}
