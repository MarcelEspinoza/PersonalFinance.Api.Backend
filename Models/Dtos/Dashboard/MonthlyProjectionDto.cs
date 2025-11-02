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
        /// Projected total income for the month.
        /// </summary>
        public decimal Income { get; set; }

        /// <summary>
        /// Projected total expense for the month.
        /// </summary>
        public decimal Expense { get; set; }

        /// <summary>
        /// Indicates whether this projection corresponds to the current month.
        /// </summary>
        public bool IsCurrent { get; set; }
    }
}