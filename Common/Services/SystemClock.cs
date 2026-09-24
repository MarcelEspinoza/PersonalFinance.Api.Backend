using PersonalFinance.Api.Common.Interfaces;

namespace PersonalFinance.Api.Common.Services
{
    /// <summary>Reloj real. En las pruebas se sustituye por uno fijo.</summary>
    public class SystemClock : IClock
    {
        public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

        public DateTime UtcNow => DateTime.UtcNow;
    }
}
