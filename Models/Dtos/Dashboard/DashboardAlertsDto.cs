namespace PersonalFinance.Api.Models.Dtos.Dashboard
{
    public class DashboardAlertsDto
    {
        public int CommitmentsOutOfRange { get; set; }
        public int BudgetsExceeded { get; set; }
        public bool NegativePlannedBalance { get; set; }
    }
}
