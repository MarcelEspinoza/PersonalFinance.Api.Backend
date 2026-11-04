using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PersonalFinance.Api.Services;

namespace PersonalFinance.Api.Tests;

/// <summary>
/// Brevo sustituye a SendGrid: el correo sale por su API y, si Brevo lo rechaza,
/// TrySendEmailAsync devuelve false para que los recordatorios se reintenten.
/// </summary>
public sealed class BrevoEmailSenderTests
{
    [Fact]
    public async Task Envia_el_correo_por_la_api_de_brevo()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created);

        Assert.True(await CreateSender(handler).TrySendEmailAsync("ana@test.local", "Asunto", "<p>Hola</p>"));

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.brevo.com/v3/smtp/email", handler.Request.RequestUri!.ToString());
        Assert.Equal("clave-de-prueba", Assert.Single(handler.Request.Headers.GetValues("api-key")));
        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("app@test.local", body.RootElement.GetProperty("sender").GetProperty("email").GetString());
        Assert.Equal("Personal Finance", body.RootElement.GetProperty("sender").GetProperty("name").GetString());
        Assert.Equal("ana@test.local", Assert.Single(body.RootElement.GetProperty("to").EnumerateArray()).GetProperty("email").GetString());
        Assert.Equal("Asunto", body.RootElement.GetProperty("subject").GetString());
        Assert.Equal("<p>Hola</p>", body.RootElement.GetProperty("htmlContent").GetString());
    }

    [Fact]
    public async Task Si_brevo_rechaza_el_correo_devuelve_false_sin_lanzar()
    {
        var handler = new RecordingHandler(HttpStatusCode.Unauthorized);

        Assert.False(await CreateSender(handler).TrySendEmailAsync("ana@test.local", "Asunto", "<p>Hola</p>"));
    }

    private static BrevoEmailSender CreateSender(RecordingHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Brevo:ApiKey"] = "clave-de-prueba",
                ["Brevo:FromEmail"] = "app@test.local"
            })
            .Build();

        return new BrevoEmailSender(new HttpClient(handler), configuration, NullLogger<BrevoEmailSender>.Instance);
    }

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }
}
