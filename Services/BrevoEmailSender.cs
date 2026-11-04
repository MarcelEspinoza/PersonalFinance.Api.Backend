using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Services
{
    /// <summary>
    /// Sends email through Brevo's transactional HTTPS API. Used instead of SendGrid when Brevo:ApiKey is set.
    /// Like SendGridEmailSender, it never throws: TrySendEmailAsync returns false so reminders are retried later.
    /// </summary>
    public class BrevoEmailSender : IEmailSender
    {
        private const string Endpoint = "https://api.brevo.com/v3/smtp/email";

        private readonly HttpClient _http;
        private readonly string? _apiKey;
        private readonly string _fromEmail;
        private readonly string _fromName;
        private readonly ILogger<BrevoEmailSender> _logger;

        public BrevoEmailSender(HttpClient http, IConfiguration configuration, ILogger<BrevoEmailSender> logger)
        {
            _http = http;
            _logger = logger;
            _apiKey = configuration["Brevo:ApiKey"];
            _fromEmail = configuration["Brevo:FromEmail"] ?? "no-reply@personalfinance.app";
            _fromName = configuration["Brevo:FromName"] ?? "Personal Finance";
        }

        public Task SendEmailAsync(string toEmail, string subject, string htmlContent) =>
            TrySendEmailAsync(toEmail, subject, htmlContent);

        public async Task<bool> TrySendEmailAsync(string toEmail, string subject, string htmlContent)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                _logger.LogWarning("Skipping email send to {Email} because Brevo API key is not configured.", toEmail);
                return false;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
                {
                    Content = JsonContent.Create(new
                    {
                        sender = new { email = _fromEmail, name = _fromName },
                        to = new[] { new { email = toEmail } },
                        subject,
                        htmlContent
                    })
                };
                request.Headers.Add("api-key", _apiKey);

                using var response = await _http.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Brevo returned status {StatusCode} when sending email to {Email}: {Body}", response.StatusCode, toEmail, body);
                    return false;
                }

                _logger.LogInformation("Email sent to {Email} via Brevo (subject: {Subject})", toEmail, subject);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email to {Email}", toEmail);
                return false;
            }
        }
    }
}
