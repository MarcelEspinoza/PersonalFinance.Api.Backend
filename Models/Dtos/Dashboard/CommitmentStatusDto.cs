namespace PersonalFinance.Api.Models.Dtos.Commitments
{
    public class CommitmentStatusDto
    {
        public Guid CommitmentId { get; set; }
        public string Name { get; set; } = string.Empty;

        public decimal ExpectedAmount { get; set; }
        public decimal ActualAmount { get; set; }

        public bool IsSatisfied { get; set; }
        public bool IsOutOfRange { get; set; }
    }
}
