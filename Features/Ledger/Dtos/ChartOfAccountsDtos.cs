using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Dtos
{
    public class ChartOfAccountsDto
    {
        public List<ChartGroupDto> Groups { get; set; } = new();
    }

    public class ChartGroupDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public ConceptKind Kind { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public List<ChartConceptDto> Concepts { get; set; } = new();
    }

    public class ChartConceptDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public ConceptNature Nature { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>Qué ha hecho realmente la siembra, para poder repetirla sin miedo.</summary>
    public class SeedChartOfAccountsResultDto
    {
        public int GroupsCreated { get; set; }
        public int ConceptsCreated { get; set; }
        public int BudgetsCreated { get; set; }
        public int GroupsAlreadyPresent { get; set; }
        public int ConceptsAlreadyPresent { get; set; }
    }
}
