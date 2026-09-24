namespace PersonalFinance.Api.Common.Exceptions
{
    /// <summary>
    /// Regla de negocio incumplida. La capa de API la traduce a 400 sin tener
    /// que distinguir excepciones genÃ©ricas del framework.
    /// </summary>
    public class BusinessRuleException : Exception
    {
        public BusinessRuleException(string message) : base(message) { }
    }

    /// <summary>El recurso no existe o no pertenece al usuario que lo pide.</summary>
    public class NotFoundException : Exception
    {
        public NotFoundException(string message) : base(message) { }
    }
}
