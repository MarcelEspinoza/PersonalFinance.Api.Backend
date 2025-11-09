using PersonalFinance.Api.Services.Contracts;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Text.RegularExpressions;

namespace PersonalFinance.Api.Services
{
    public class SendGridEmailSender : IEmailSender
    {
        private readonly string? _apiKey;
        private readonly EmailAddress _from;
        private readonly bool _sandboxMode;
        private readonly ILogger<SendGridEmailSender> _logger;

        public SendGridEmailSender(IConfiguration configuration, ILogger<SendGridEmailSender> logger)
        {
            _logger = logger;
            _apiKey = configuration["SendGrid:ApiKey"];
            var fromEmail = configuration["SendGrid:FromEmail"] ?? "no-reply@personalfinance.app";
            var fromName = configuration["SendGrid:FromName"] ?? "Personal Finance";
            _from = new EmailAddress(fromEmail, fromName);
            _sandboxMode = bool.TryParse(configuration["SendGrid:SandboxMode"], out var s) && s;

            // Log presence (true/false) but never log the secret value.
            _logger.LogInformation("SendGrid configured: {HasKey}", !string.IsNullOrWhiteSpace(_apiKey));
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                _logger.LogWarning("SendGrid API key is not configured. Email will not be sent.");
            }
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlContent)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                _logger.LogWarning("Skipping email send to {Email} because SendGrid API key is not configured.", toEmail);
                return;
            }

            try
            {
                var client = new SendGridClient(_apiKey);
                var msg = MailHelper.CreateSingleEmail(
                    _from,
                    new EmailAddress(toEmail),
                    subject,
                    plainTextContent: "",
                    htmlContent: htmlContent
                );

                if (_sandboxMode)
                {
                    msg.MailSettings = msg.MailSettings ?? new MailSettings();
                    msg.MailSettings.SandboxMode = new SandboxMode { Enable = true };
                }

                var response = await client.SendEmailAsync(msg);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("SendGrid returned status {StatusCode} when sending email to {Email}", response.StatusCode, toEmail);
                }
                else
                {
                    _logger.LogInformation("Email sent to {Email} via SendGrid (subject: {Subject})", toEmail, subject);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email to {Email}", toEmail);
            }
        }
    

        // Optional helper to send using a Dynamic Template in SendGrid
        public async Task SendTemplateAsync(string toEmail, string templateId, object templateData)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                _logger.LogWarning("SendGrid API key is not configured. Template email will not be sent.");
                throw new InvalidOperationException("SendGrid:ApiKey not configured");
            }

            var client = new SendGridClient(_apiKey);
            var msg = new SendGridMessage();
            msg.SetFrom(_from);
            msg.AddTo(toEmail);
            msg.SetTemplateId(templateId);
            msg.SetTemplateData(templateData);

            ApplySandboxModeIfEnabled(msg);

            var response = await client.SendEmailAsync(msg);
            if ((int)response.StatusCode >= 400)
            {
                var body = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid template send failed. Status: {Status}, Body: {Body}", response.StatusCode, body);
                throw new InvalidOperationException($"SendGrid template send failed: {response.StatusCode} - {body}");
            }

            _logger.LogInformation("SendGrid template email queued/sent to {To}. Status: {Status}", toEmail, response.StatusCode);
        }

        private void ApplySandboxModeIfEnabled(SendGridMessage msg)
        {

            if (_sandboxMode)
            {
                msg.MailSettings = new MailSettings
                {
                    SandboxMode = new SandboxMode { Enable = true }
                };
                _logger.LogInformation("SendGrid sandbox mode enabled - emails will NOT be delivered.");
            }
        }
    }
}