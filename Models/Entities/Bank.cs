namespace PersonalFinance.Api.Models.Entities
{
    using System.ComponentModel.DataAnnotations;

    public class Bank
    {
        public Guid Id { get; set; }

        [Required]
        public Guid UserId { get; set; } // propietario

        [Required]
        public string Name { get; set; } = string.Empty;

        public string? Institution { get; set; }
        public string? AccountNumber { get; set; }
        public string? Currency { get; set; } = "EUR";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
