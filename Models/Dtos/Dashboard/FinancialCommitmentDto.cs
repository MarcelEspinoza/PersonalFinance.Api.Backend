using PersonalFinance.Api.Models.Enums;

namespace PersonalFinance.Api.Models.Dtos.Dashboard
{
    public class FinancialCommitmentDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public CommitmentType Type { get; set; }
        public CommitmentNature Nature { get; set; }
        public decimal AmountExpected { get; set; }
        public int CategoryId { get; set; }
        public Guid? BankId { get; set; }
        public bool IsActive { get; set; }
    }

}
