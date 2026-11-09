namespace PersonalFinance.Api.Services
{
    /// <summary>Rules shared by every way of creating a pasanaco with its numbers in one step.</summary>
    public static class PasanacoSetup
    {
        public const int MaxNameLength = 100;
        public const int MaxParticipants = 60;

        /// <summary>Returns a Spanish error, or null when the pasanaco can be created.</summary>
        public static string? Validate(
            string? name, decimal monthlyAmount, int startMonth, int startYear, int currentRound,
            IReadOnlyList<(int Number, string? Name)> participants)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
                return "Indica un nombre para el pasanaco (máximo 100 caracteres).";
            if (monthlyAmount <= 0m || decimal.Round(monthlyAmount, 2) != monthlyAmount)
                return "La cuota mensual debe ser mayor que 0.";
            if (startMonth is < 1 or > 12 || startYear < 2000)
                return "Indica el mes y el año del primer turno.";
            if (participants.Count < 2 || participants.Count > MaxParticipants)
                return $"El pasanaco debe tener entre 2 y {MaxParticipants} números.";
            if (participants.Any(p => string.IsNullOrWhiteSpace(p.Name) || p.Name.Trim().Length > MaxNameLength))
                return "Cada número necesita el nombre de quien lo tiene.";
            if (!participants.Select(p => p.Number).OrderBy(n => n).SequenceEqual(Enumerable.Range(1, participants.Count)))
                return $"Los números deben ir del 1 al {participants.Count}, sin repetir ni saltar ninguno.";
            if (currentRound < 1 || currentRound > participants.Count)
                return $"El turno actual debe estar entre 1 y {participants.Count}.";
            return null;
        }
    }
}
