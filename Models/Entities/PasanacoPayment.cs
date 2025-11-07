namespace PersonalFinance.Api.Models.Entities
{
    public class PasanacoPayment
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string PasanacoId { get; set; } = string.Empty;
        public Pasanaco Pasanaco { get; set; } = null!;

        public string ParticipantId { get; set; } = string.Empty;
        public Participant Participant { get; set; } = null!;

        public int Month { get; set; }
        public int Year { get; set; }

        public bool Paid { get; set; } = false;
        public DateTime? PaymentDate { get; set; }
        public int? TransactionId { get; set; }
        public Guid PaidByLoanId { get; internal set; }
    }
}
