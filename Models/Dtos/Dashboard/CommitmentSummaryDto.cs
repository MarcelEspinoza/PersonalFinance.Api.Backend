namespace PersonalFinance.Api.Models.Dtos.Dashboard
{
    public class CommitmentSummaryDto
    {
        public int Total { get; set; }
        public int Ok { get; set; }
        public int Pending { get; set; }
        public int OutOfRange { get; set; }
    }
}
