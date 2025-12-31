namespace PersonalFinance.Api.Models.Dtos.Dashboard
{
    public class DashboardAlertsDto
    {
        public bool HasCriticalAlerts { get; set; }

        public List<AlertItemDto> Items { get; set; } = new();
    }

    public class AlertItemDto
    {
        // "Budget" | "Commitment" | "Balance"
        public string Type { get; set; } = string.Empty;

        // Texto humano
        public string Message { get; set; } = string.Empty;

        // Para navegación frontend
        public string? Action { get; set; }
    }
}
