namespace PersonalFinance.Api.Services.Contracts
{
    public interface IEmailSender
    {
        /// <summary>
        /// Envía un email HTML.
        /// </summary>
        Task SendEmailAsync(string toEmail, string subject, string htmlContent);
    }
}
