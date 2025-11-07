using System.ComponentModel.DataAnnotations;

namespace PersonalFinance.Api.Models.Dtos.Pasanaco
{
    public class CreateLoanForParticipantDto
    {
        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "El importe debe ser mayor que 0")]
        public decimal Amount { get; set; }

        public string? Note { get; set; }
    }
}
