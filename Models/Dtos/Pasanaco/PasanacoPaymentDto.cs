namespace PersonalFinance.Api.Models.Dtos.Pasanaco
{
    public class PasanacoPaymentDto
    {
        public string Id { get; set; } = string.Empty;
        public string PasanacoId { get; set; } = string.Empty;
        public string ParticipantId { get; set; } = string.Empty;
        public int Month { get; set; }
        public int Year { get; set; }
        public bool Paid { get; set; }
        public DateTime? PaymentDate { get; set; }
        public int? TransactionId { get; set; }
    }

}
