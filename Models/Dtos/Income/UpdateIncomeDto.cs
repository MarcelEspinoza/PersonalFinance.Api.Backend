using PersonalFinance.Api.Enums;

namespace PersonalFinance.Api.Models.Dtos.Income
{
    public class UpdateIncomeDto
    {
        public decimal? Amount { get; set; }
        public string? Description { get; set; }
        public DateTime? Date { get; set; }

        // reference by id; optional when updating
        public int? CategoryId { get; set; }

        public IncomeType Type { get; set; } = IncomeType.Fixed;
    }
}
