namespace PersonalFinance.Api.Models.Dtos.Pasanaco
{
    /// <summary>Cuenta donde entró el dinero de una cuota (por ejemplo, Efectivo). Vacío si no se apunta en ninguna.</summary>
    public class QuotaPaidDto
    {
        public Guid? AccountId { get; set; }

        public Guid? ConceptId { get; set; }

        public DateOnly? Date { get; set; }
    }
}
