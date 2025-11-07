using Microsoft.EntityFrameworkCore;

namespace PersonalFinance.Api.Models.Entities
{
    [Index(nameof(PasanacoId), nameof(AssignedNumber), IsUnique = true)]
    public class Participant
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public int AssignedNumber { get; set; }
        public bool HasReceived { get; set; } = false;

        public string PasanacoId { get; set; } = string.Empty;
        public Pasanaco Pasanaco { get; set; } = null!;

        public ICollection<PasanacoPayment> Payments { get; set; } = new List<PasanacoPayment>();
    }
}
