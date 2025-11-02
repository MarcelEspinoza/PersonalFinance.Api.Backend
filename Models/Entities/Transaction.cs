using Microsoft.EntityFrameworkCore;

namespace PersonalFinance.Api.Models.Entities
{
    public class Transaction
    {
        public int Id { get; set; }

        public Guid UserId { get; set; } // ← debe coincidir con el tipo de clave en Users

        public DateTime Date { get; set; }

        [Precision(18, 2)]
        public decimal Amount { get; set; }

        public string Type { get; set; } = "income";

        public string? Description { get; set; }
    }


}
