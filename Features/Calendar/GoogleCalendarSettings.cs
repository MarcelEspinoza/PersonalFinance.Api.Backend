using PersonalFinance.Api.Common.Exceptions;

namespace PersonalFinance.Api.Features.Calendar
{
    public sealed class GoogleCalendarSettings(IConfiguration configuration)
    {
        public string ClientId => configuration["GoogleCalendar:ClientId"] ?? string.Empty;
        public string ClientSecret => configuration["GoogleCalendar:ClientSecret"] ?? string.Empty;
        public string RedirectUri => configuration["GoogleCalendar:RedirectUri"] ?? string.Empty;
        public string FrontendUrl => configuration["GoogleCalendar:FrontendUrl"] ?? string.Empty;
        public string TimeZone => configuration["GoogleCalendar:TimeZone"] ?? "Europe/Madrid";

        public bool IsConfigured
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret)
                    || !IsWebUri(RedirectUri) || !IsWebUri(FrontendUrl)) return false;
                var key = configuration["GoogleCalendar:TokenEncryptionKey"];
                if (string.IsNullOrWhiteSpace(key)) return false;
                Span<byte> bytes = stackalloc byte[64];
                return Convert.TryFromBase64String(key, bytes, out var length) && length == 32
                    && TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone, out _);
            }
        }

        public byte[] EncryptionKey()
        {
            EnsureConfigured();
            return Convert.FromBase64String(configuration["GoogleCalendar:TokenEncryptionKey"]!);
        }

        public void EnsureConfigured()
        {
            if (!IsConfigured)
                throw new BusinessRuleException("Falta configurar la conexión con Google Calendar en el servidor.");
        }

        public string ReturnUrl(string returnPath, string result)
        {
            EnsureConfigured();
            var path = returnPath is "/pasanaco" or "/reminders" ? returnPath : "/zeus";
            return new Uri(new Uri(FrontendUrl), path + "?googleCalendar=" + result).AbsoluteUri;
        }

        private static bool IsWebUri(string value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps
                || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
            && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment);
    }
}
