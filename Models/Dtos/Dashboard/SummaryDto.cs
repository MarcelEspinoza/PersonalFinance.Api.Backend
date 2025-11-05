namespace PersonalFinance.Api.Models.Dtos.Dashboard
{
    public class SummaryDto
    {
        public decimal TotalIncome { get; set; }
        public decimal TotalExpense { get; set; }
        public decimal Balance { get; set; }
        public decimal Savings { get; set; }

        // Ahorro proyectado para meses futuros (y opcionalmente para el actual si quieres un objetivo)
        public decimal ProjectedSavings { get; set; }

        public decimal PlannedBalance { get; set; }
    }

}
