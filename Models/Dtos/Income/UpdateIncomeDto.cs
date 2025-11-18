using System.ComponentModel.DataAnnotations.Schema;

namespace PersonalFinance.Api.Models.Dtos.Income;
public class UpdateIncomeDto
{
    public decimal? Amount { get; set; }
    public string? Description { get; set; }
    public DateTime? Date { get; set; }
    public int? CategoryId { get; set; }
    public string? Type { get; set; }
    public DateTime? Start_Date { get; set; }
    public DateTime? End_Date { get; set; }
    public string? Notes { get; set; }

    // 👇 opcional
    public Guid? LoanId { get; set; }
    public bool? IsIndefinite { get; set; }

    public Guid? BankId { get; set; }
}
