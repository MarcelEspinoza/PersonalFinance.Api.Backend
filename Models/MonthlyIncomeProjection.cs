namespace PersonalFinance.Api.Models
{
    public class MonthlyIncomeProjection
    {
        public string Month { get; set; } = string.Empty; // Ej: "Noviembre 2025"
        public decimal TotalIncome { get; set; }
    }

}
