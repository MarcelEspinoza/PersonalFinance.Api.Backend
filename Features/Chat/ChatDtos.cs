namespace PersonalFinance.Api.Features.Chat
{
    public sealed class ChatMessageDto
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public sealed class ChatRequestDto
    {
        public string Message { get; set; } = string.Empty;
        public List<ChatMessageDto> History { get; set; } = new();
    }

    /// <summary>
    /// Acción concreta que el asistente propone (crear un gasto/ingreso). Nunca
    /// se ejecuta sola: el frontend la muestra como tarjeta de confirmación y
    /// sólo se aplica si el usuario pulsa "Confirmar", llamando a /api/chat/actions/*.
    /// </summary>
    public sealed class ProposedActionDto
    {
        public string Type { get; set; } = string.Empty; // "create_expense" | "create_income"
        public string Summary { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty; // yyyy-MM-dd
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string ExpenseType { get; set; } = "Temporary"; // Fixed | Temporary
    }

    public sealed class ChatResponseDto
    {
        public string Reply { get; set; } = string.Empty;
        public List<ProposedActionDto> ProposedActions { get; set; } = new();
    }

    public sealed class ConfirmActionDto
    {
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public string ExpenseType { get; set; } = "Temporary";
    }
}
