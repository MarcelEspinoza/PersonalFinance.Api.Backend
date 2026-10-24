namespace PersonalFinance.Api.Features.ServiceSavings
{
    /// <summary>User-facing texts of the service savings feature.</summary>
    internal static class ServiceSavingsMessages
    {
        public const string DefaultError = "El servicio de IA no ha respondido; inténtalo de nuevo en un momento.";
        public const string MissingApiKey = "Falta configurar la clave de Anthropic en el servidor.";
        public const string WebSearchDisabled =
            "La búsqueda web no está activada en la cuenta de Anthropic. Actívala en la consola de Anthropic y vuelve a intentarlo.";
        public const string InvalidResponse = "La IA no ha devuelto un resultado válido; inténtalo de nuevo en un momento.";
        public const string Interrupted = "La búsqueda se interrumpió antes de terminar. Vuelve a intentarlo.";
        public const string NothingToReview =
            "No hemos encontrado pagos recurrentes que revisar. Cuando tengas recibos o suscripciones registrados, vuelve a intentarlo.";
        public const string NoImprovements = "No hemos encontrado mejoras claras en tus pagos recurrentes.";
        public const string UnverifiedOffer = "No hemos podido comprobar esta oferta en internet; revísala antes de cambiar.";

        public static string DailyLimitReached(int used) =>
            $"Ya has hecho {used} análisis hoy. Podrás volver a buscar mañana.";
    }

    /// <summary>A failure whose message can be shown to the user as is.</summary>
    internal sealed class ServiceSavingsFailure : Exception
    {
        public ServiceSavingsFailure(string userMessage) : base(userMessage) { }
    }
}
