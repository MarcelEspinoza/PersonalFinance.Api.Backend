using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PersonalFinance.Api.Features.Chat;
using PersonalFinance.Api.Models.Dtos.Dashboard;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Tests;

/// <summary>
/// El usuario puede contestar a cada consejo; esas respuestas se guardan y se
/// devuelven al modelo en el siguiente análisis para que no repita lo mismo.
/// </summary>
public sealed class MonthAdviceFeedbackTests : LedgerTestBase
{
    [Fact]
    public async Task Responder_a_un_consejo_guarda_el_hilo_y_devuelve_la_respuesta()
    {
        var handler = new RecordingHandler(
            """{"content":[{"type":"text","text":"{\"reply\":\"Entendido: la nómina entra el 1, lo recalculo.\"}"}]}""");
        var service = CreateService(handler);

        var result = await service.ReplyAsync(
            UserId,
            new AdviceReplyRequestDto
            {
                Year = 2026,
                Month = 10,
                InsightTitle = "Tu nómina no aparece",
                InsightDetail = "No veo la nómina en octubre.",
                Message = "Sí que está: entra el día 1 en Revolut."
            },
            CancellationToken.None);

        Assert.True(result.Available);
        Assert.Equal("Entendido: la nómina entra el 1, lo recalculo.", result.Reply);
        var note = Assert.Single(result.Thread);
        Assert.Equal("Sí que está: entra el día 1 en Revolut.", note.Message);

        var stored = Assert.Single(Db.AdviceNotes.ToList());
        Assert.Equal("Tu nómina no aparece", stored.InsightTitle);
        Assert.Equal(UserId, stored.UserId);

        using var request = JsonDocument.Parse(handler.RequestBody!);
        var content = request.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
        using var sent = JsonDocument.Parse(content!);
        Assert.Equal("Tu nómina no aparece", sent.RootElement.GetProperty("consejo").GetProperty("titulo").GetString());
        Assert.Equal(
            "Sí que está: entra el día 1 en Revolut.",
            sent.RootElement.GetProperty("respuestaDelUsuario").GetString());
    }

    [Fact]
    public async Task El_analisis_siguiente_recibe_lo_que_dijo_el_usuario()
    {
        var replyHandler = new RecordingHandler(
            """{"content":[{"type":"text","text":"{\"reply\":\"Anotado.\"}"}]}""");
        await CreateService(replyHandler).ReplyAsync(
            UserId,
            new AdviceReplyRequestDto
            {
                Year = 2026,
                Month = 10,
                InsightTitle = "Mueve el alquiler",
                Message = "El alquiler no lo puedo mover, es domiciliado."
            },
            CancellationToken.None);

        var adviceHandler = new RecordingHandler(
            """{"content":[{"type":"text","text":"{\"summary\":\"Todo controlado.\",\"insights\":[]}"}]}""");

        var advice = await CreateService(adviceHandler).AdviseAsync(UserId, 2026, 10, CancellationToken.None);

        Assert.True(advice.Available);
        using var request = JsonDocument.Parse(adviceHandler.RequestBody!);
        var content = request.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
        Assert.Contains("El alquiler no lo puedo mover", content);
        Assert.Contains("Mueve el alquiler", content);
        Assert.Contains("userFeedback", content);
        Assert.Contains("no repitas ese consejo", request.RootElement.GetProperty("system").GetString());
    }

    [Fact]
    public async Task Un_mensaje_vacio_no_llama_al_modelo()
    {
        var handler = new RecordingHandler("""{"content":[{"type":"text","text":"{}"}]}""");

        var result = await CreateService(handler).ReplyAsync(
            UserId,
            new AdviceReplyRequestDto { Year = 2026, Month = 10, InsightTitle = "X", Message = "   " },
            CancellationToken.None);

        Assert.False(result.Available);
        Assert.Null(handler.RequestBody);
        Assert.Empty(Db.AdviceNotes.ToList());
    }

    private MonthAdvisorService CreateService(RecordingHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Anthropic:ApiKey"] = "test-key" })
            .Build();

        return new MonthAdvisorService(
            new HttpClient(handler),
            configuration,
            new StubDashboardService(),
            Db,
            NullLogger<MonthAdvisorService>.Instance);
    }

    private sealed class StubDashboardService : IDashboardService
    {
        public Task<DashboardProjectionResult> GetFutureProjectionAsync(
            int? year = null,
            int? month = null,
            CancellationToken ct = default,
            bool includeVariableReserve = true)
        {
            return Task.FromResult(new DashboardProjectionResult
            {
                Outlook = new MonthOutlookDto { Year = year ?? 2026, Month = month ?? 10 }
            });
        }
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
