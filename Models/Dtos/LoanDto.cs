
using System;
using System.Collections.Generic;
using PersonalFinance.Api.Models.Dtos.Category;
using PersonalFinance.Api.Models.Dtos.Pasanaco;
using PersonalFinance.Api.Models.Enums;

namespace PersonalFinance.Api.Models.Dtos
{
    public class LoanDto
    {
        public Guid UserId { get; set; }

        // Type / basic info
        public LoanType Type { get; set; }
        public string Name { get; set; } = string.Empty;

        // Amounts
        public decimal PrincipalAmount { get; set; }
        public decimal OutstandingAmount { get; set; }

        // Dates
        public DateTimeOffset StartDate { get; set; }
        public DateTimeOffset? DueDate { get; set; }

        // Status
        public string Status { get; set; } = "active";

        // Bank-specific fields (nullable)
        public decimal? InterestRate { get; set; }
        public decimal? TAE { get; set; }
        public int? InstallmentsPaid { get; set; }
        public int? InstallmentsRemaining { get; set; }
        public decimal? NextPaymentAmount { get; set; }
        public DateTimeOffset? NextPaymentDate { get; set; }

        // Category relation
        public int CategoryId { get; set; }
        public CreateCategoryDto? Category { get; set; }

        // Payments collection
        public List<LoanPaymentDto> Payments { get; set; } = new List<LoanPaymentDto>();

        // Pasanaco relation
        public string? PasanacoId { get; set; }
        public PasanacoDto? Pasanaco { get; set; }
    }

}
