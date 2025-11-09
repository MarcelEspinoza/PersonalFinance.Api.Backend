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
    }
}