using PersonalFinance.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace PersonalFinance.Api.Models.Dtos.Income
{        // Income DTOs updated to reference Category by id (relation)
    public class CreateIncomeDto
    {
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string Type { get; set; } = string.Empty;
        public int CategoryId { get; set; }   
        public DateTime? Start_Date { get; set; }
        public DateTime? End_Date { get; set; }
        public string? Notes { get; set; }
    }


}
