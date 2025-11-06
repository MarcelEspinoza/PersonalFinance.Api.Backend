namespace PersonalFinance.Api.Models.Dtos.Pasanaco
{
    public class CreatePasanacoDto
    {
        public string Name { get; set; } = string.Empty;
        public decimal MonthlyAmount { get; set; }
        public int TotalParticipants { get; set; }
        public int CurrentRound { get; set; } = 1;
        public int StartMonth { get; set; }
        public int StartYear { get; set; }
    }
}
