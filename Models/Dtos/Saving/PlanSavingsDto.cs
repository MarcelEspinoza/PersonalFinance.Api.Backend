namespace PersonalFinance.Api.Models.Dtos.Saving
{
    public class PlanSavingsDto
    {
        public decimal MonthlyAmount { get; set; }   // cuánto quieres ahorrar cada mes
        public int Months { get; set; }              // cuántos meses quieres planificar
        public DateTime? StartDate { get; set; }     // opcional: desde qué mes empezar
    }

}
