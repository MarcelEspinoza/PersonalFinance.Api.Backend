using PersonalFinance.Api.Enums;

namespace PersonalFinance.Api.Models.Dtos.Expense
{
    public class UpdateExpenseDto
    {
        public decimal? Amount { get; set; }
        public string? Description { get; set; }
        public DateTime? Date { get; set; }
        public int? CategoryId { get; set; }

        // Mejor nullable si no siempre quieres forzar el cambio
        public string? Type { get; set; }

        public DateTime? Start_Date { get; set; }
        public DateTime? End_Date { get; set; }
        public string? Notes { get; set; }
    }

}
