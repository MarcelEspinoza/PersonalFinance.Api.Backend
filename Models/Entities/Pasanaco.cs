using System.ComponentModel.DataAnnotations.Schema;

namespace PersonalFinance.Api.Models.Entities
{
    public class Pasanaco
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public required string Name
        {
            get; set;
        }
        [Column(TypeName = "decimal(18,2)")]
        public required decimal MonthlyAmount { get; set; }
        public required int TotalParticipants { get; set; }
        public required int CurrentRound
        {
            get; set;
        }
        public required int StartMonth { get; set; } // 1–12
        public required int StartYear { get; set; }

        public ICollection<Participant> Participants { get; set; } = new List<Participant>();
        public ICollection<PasanacoPayment> Payments { get; set; } = new List<PasanacoPayment>();

    }

}
