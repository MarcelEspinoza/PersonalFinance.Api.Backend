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
    /// Acción concreta que el asistente propone. Nunca
    /// se ejecuta sola: el frontend la muestra como tarjeta de confirmación y
    /// sólo se aplica si el usuario pulsa "Confirmar".
    /// </summary>
    public sealed class ProposedActionDto
    {
        public string Type { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty; // yyyy-MM-dd
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string ExpenseType { get; set; } = "Temporary"; // Fixed | Temporary
        public string? TargetId { get; set; }
        public string? Name { get; set; }
        public string? Notes { get; set; }
        public int? Year { get; set; }
        public int? Month { get; set; }
        public Guid? ConceptId { get; set; }
        public Guid? AccountId { get; set; }
        public string? NormalizedDescription { get; set; }
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
