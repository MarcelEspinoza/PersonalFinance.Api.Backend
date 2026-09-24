namespace PersonalFinance.Api.Common.Interfaces
{
    /// <summary>
    /// La fecha de hoy, inyectada en lugar de leÃ­da de DateTime.UtcNow, para
    /// que los casos de uso que dependen del calendario se puedan probar.
    /// </summary>
    public interface IClock
    {
        DateOnly Today { get; }
        DateTime UtcNow { get; }
    }
}
