using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PersonalFinance.Api.Models.Entities
{
    public class Pasanaco
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        [Required]
        public string Name
        {
            get; set;
        }
        [Column(TypeName = "decimal(18,2)")]
        [Required]
        public decimal MonthlyAmount { get; set; }
        [Required]
        public int TotalParticipants { get; set; }
        [Required]
        public int CurrentRound
        {
            get; set;
        }
        [Required]
        public int StartMonth { get; set; } // 1–12
        [Required]
        public int StartYear { get; set; }

        public ICollection<Participant> Participants { get; set; } = new List<Participant>();
        public ICollection<PasanacoPayment> Payments { get; set; } = new List<PasanacoPayment>();
    }

}
