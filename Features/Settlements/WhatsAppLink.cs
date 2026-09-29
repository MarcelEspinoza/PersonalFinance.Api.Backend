namespace PersonalFinance.Api.Features.Settlements
{
    /// <summary>
    /// Enlace wa.me: abre el chat de WhatsApp con el mensaje ya escrito. No
    /// envía nada por su cuenta, el último paso siempre lo da la persona.
    /// </summary>
    public static class WhatsAppLink
    {
        public static string? Build(string? phoneNumber, string message)
        {
            var digits = new string((phoneNumber ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digits.Length == 0) return null;

            return $"https://wa.me/{digits}?text={Uri.EscapeDataString(message)}";
        }
    }
}
