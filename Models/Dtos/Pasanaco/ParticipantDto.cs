namespace PersonalFinance.Api.Models.Dtos.Pasanaco
{
    public class ParticipantDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int AssignedNumber { get; set; }
        public bool HasReceived { get; set; }
    }

}
