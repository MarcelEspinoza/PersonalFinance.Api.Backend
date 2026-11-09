namespace PersonalFinance.Api.Models.Dtos.Pasanaco
{
    /// <summary>
    /// Pasanaco created in one step with every assigned number. TotalParticipants is the number
    /// of entries in <see cref="Participants"/>; the same person may hold several numbers.
    /// </summary>
    public class CreatePasanacoWithParticipantsDto
    {
        public string Name { get; set; } = string.Empty;
        public decimal MonthlyAmount { get; set; }
        public int StartMonth { get; set; }
        public int StartYear { get; set; }
        public int CurrentRound { get; set; } = 1;
        public List<CreateParticipantDto> Participants { get; set; } = new();
    }
}
