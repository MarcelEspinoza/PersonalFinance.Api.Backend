namespace PersonalFinance.Api.Services.Contracts
{
    public interface IEmailSender
    {
        /// <summary>
        /// Envía un email HTML.
        /// </summary>
        Task SendEmailAsync(string toEmail, string subject, string htmlContent);

        /// <summary>
        /// Igual que SendEmailAsync, pero indica si el proveedor aceptó el correo.
        /// </summary>
        async Task<bool> TrySendEmailAsync(string toEmail, string subject, string htmlContent)
        {
            await SendEmailAsync(toEmail, subject, htmlContent);
            return true;
        }
    }
}
