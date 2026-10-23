namespace PersonalFinance.Api.Services
{
    public static class EmailTemplates
    {
        public static string ConfirmEmailTemplate(string fullName, string callbackUrl)
        {
            // Plantilla HTML responsiva con estilo inline.
            // Puedes adaptar colores, logo y textos a tu branding.
            return $@"
<!doctype html>
<html>
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
  <title>Confirma tu correo</title>
</head>
<body style=""margin:0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial; background:#f4f6f8;"">
  <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""padding:40px 0;"">
    <tr>
      <td align=""center"">
        <table width=""600"" cellpadding=""0"" cellspacing=""0"" style=""background:#ffffff; border-radius:12px; overflow:hidden; box-shadow:0 8px 30px rgba(18,32,47,0.08);"">
          <tr>
            <td style=""background:linear-gradient(90deg,#4f46e5,#06b6d4); padding:28px; text-align:center; color:white;"">
              <h1 style=""margin:0; font-size:22px;"">Bienvenido a Personal Finance</h1>
            </td>
          </tr>

          <tr>
            <td style=""padding:32px;"">
              <p style=""font-size:16px; color:#253047; margin:0 0 16px;"">Hola {System.Net.WebUtility.HtmlEncode(fullName ?? "usuario")},</p>
              <p style=""color:#425466; font-size:15px; line-height:1.5; margin:0 0 20px;"">
                Gracias por registrarte en Personal Finance. Para terminar la creación de tu cuenta necesitamos que confirmes tu correo.
              </p>

              <p style=""text-align:center; margin:26px 0;"">
                <a href=""{callbackUrl}"" style=""display:inline-block; background:#2563eb; color:white; text-decoration:none; padding:12px 22px; border-radius:8px; font-weight:600;"">
                  Confirmar correo
                </a>
              </p>

              <p style=""color:#9aa8ba; font-size:13px; margin:0 0 8px;"">
                Si el botón no funciona, copia y pega la siguiente URL en tu navegador:
              </p>
              <p style=""word-break:break-all; color:#0f1724; font-size:13px; line-height:1.35;"">
                <a href=""{callbackUrl}"" style=""color:#2563eb; text-decoration:underline;"">{callbackUrl}</a>
              </p>

              <hr style=""border:none; border-top:1px solid #eef2f6; margin:24px 0;"">

              <p style=""color:#6b7280; font-size:13px; margin:0;"">
                ¿No te registraste en Personal Finance? Ignora este correo y no se confirmará ninguna cuenta.
              </p>
            </td>
          </tr>

          <tr>
            <td style=""background:#f8fafc; padding:18px; text-align:center; color:#94a3b8; font-size:13px;"">
              Personal Finance • Gestiona tus finanzas • &copy; {DateTime.UtcNow.Year}
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>
";
        }

        public static string NewRegistrationAdminTemplate(string? fullName, string? email)
        {
            var name = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(fullName) ? "(sin nombre)" : fullName);
            var mail = System.Net.WebUtility.HtmlEncode(email ?? string.Empty);

            return Layout("Nueva solicitud de registro", $@"
              <p style=""font-size:16px; color:#253047; margin:0 0 16px;"">Hola,</p>
              <p style=""color:#425466; font-size:15px; line-height:1.5; margin:0 0 16px;"">
                Se ha registrado una nueva cuenta en Personal Finance y está pendiente de tu aprobación:
              </p>
              <p style=""color:#0f1724; font-size:15px; line-height:1.6; margin:0 0 16px;"">
                <strong>Nombre:</strong> {name}<br>
                <strong>Correo:</strong> {mail}
              </p>
              <p style=""color:#425466; font-size:15px; line-height:1.5; margin:0;"">
                Para darle acceso, apruébala en <strong>Ajustes → Administración</strong>. Si no reconoces esta solicitud, no la apruebes.
              </p>");
        }

        public static string AccountApprovedTemplate(string? fullName)
        {
            var name = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(fullName) ? "usuario" : fullName);

            return Layout("Tu cuenta ha sido aprobada", $@"
              <p style=""font-size:16px; color:#253047; margin:0 0 16px;"">Hola {name},</p>
              <p style=""color:#425466; font-size:15px; line-height:1.5; margin:0;"">
                Tu cuenta en Personal Finance ha sido aprobada. Ya puedes iniciar sesión con tu correo y contraseña.
              </p>");
        }

        public static string PasswordResetTemplate(string? fullName, string email, string token, string? resetUrl)
        {
            var name = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(fullName) ? "usuario" : fullName);
            var action = string.IsNullOrWhiteSpace(resetUrl)
                ? $@"<p style=""color:#425466; font-size:15px; line-height:1.5; margin:0 0 12px;"">Usa este código para restablecerla:</p>
              <p style=""word-break:break-all; color:#0f1724; font-size:13px; line-height:1.35; background:#f8fafc; padding:12px; border-radius:8px;"">{System.Net.WebUtility.HtmlEncode(token)}</p>"
                : $@"<p style=""text-align:center; margin:26px 0;"">
                <a href=""{System.Net.WebUtility.HtmlEncode($"{resetUrl}?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}")}"" style=""display:inline-block; background:#2563eb; color:white; text-decoration:none; padding:12px 22px; border-radius:8px; font-weight:600;"">
                  Restablecer contraseña
                </a>
              </p>";

            return Layout("Restablece tu contraseña", $@"
              <p style=""font-size:16px; color:#253047; margin:0 0 16px;"">Hola {name},</p>
              <p style=""color:#425466; font-size:15px; line-height:1.5; margin:0 0 16px;"">
                Hemos recibido una solicitud para restablecer la contraseña de tu cuenta en Personal Finance.
              </p>
              {action}
              <p style=""color:#6b7280; font-size:13px; margin:16px 0 0;"">
                Si no lo has solicitado tú, ignora este correo: tu contraseña no cambiará.
              </p>");
        }

        private static string Layout(string title, string bodyHtml)
        {
            return $@"
<!doctype html>
<html>
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
  <title>{title}</title>
</head>
<body style=""margin:0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial; background:#f4f6f8;"">
  <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""padding:40px 0;"">
    <tr>
      <td align=""center"">
        <table width=""600"" cellpadding=""0"" cellspacing=""0"" style=""background:#ffffff; border-radius:12px; overflow:hidden; box-shadow:0 8px 30px rgba(18,32,47,0.08);"">
          <tr>
            <td style=""background:linear-gradient(90deg,#4f46e5,#06b6d4); padding:28px; text-align:center; color:white;"">
              <h1 style=""margin:0; font-size:22px;"">{title}</h1>
            </td>
          </tr>
          <tr>
            <td style=""padding:32px;"">{bodyHtml}
            </td>
          </tr>
          <tr>
            <td style=""background:#f8fafc; padding:18px; text-align:center; color:#94a3b8; font-size:13px;"">
              Personal Finance • Gestiona tus finanzas • &copy; {DateTime.UtcNow.Year}
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>
";
        }
    }
}