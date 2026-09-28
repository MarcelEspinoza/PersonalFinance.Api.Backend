using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PersonalFinance.Api.Features.Ledger.Common;

namespace PersonalFinance.Api.Tests;

public sealed class ImportChatServiceTests
{
    [Fact]
    public async Task Mantiene_separadas_las_filas_y_prioriza_la_aclaracion_mas_reciente()
    {
        var handler = new RecordingHandler(
            """{"content":[{"type":"text","text":"{\"reply\":\"Sí, son dos movimientos separados.\",\"changes\":[],\"unrecognized\":[]}"}]}""");
        var service = CreateService(handler);
        var history = new[]
        {
            new ImportChatMessage("user", "El ingreso de 70 euros es un préstamo; el de 25,44 es para Glovo."),
            new ImportChatMessage("assistant", "Ambos ingresos son préstamos."),
            new ImportChatMessage("user", "¿No deberían ser dos registros por separado?")
        };

        var result = await service.AskAsync(
            history[^1].Content,
            history[..^1],
            new[]
            {
                new ImportChatRowContext(Guid.NewGuid(), 58, "2026-09-25", 70m, "Transferencia recibida", null),
                new ImportChatRowContext(Guid.NewGuid(), 69, "2026-09-27", 25.44m, "Transferencia recibida", null)
            },
            new[]
            {
                new ImportAiCandidate(Guid.NewGuid(), "Préstamos cobrados", "Income"),
                new ImportAiCandidate(Guid.NewGuid(), "Reembolsos y devoluciones", "Income")
            },
            CancellationToken.None);

        Assert.Equal("Sí, son dos movimientos separados.", result.Reply);
        Assert.Empty(result.Changes);

        using var request = JsonDocument.Parse(handler.RequestBody!);
        var system = request.RootElement.GetProperty("system").GetString();
        Assert.Contains("nunca agrupes", system);
        Assert.Contains("La aclaración más reciente", system);
        var messages = request.RootElement.GetProperty("messages");
        Assert.Equal(3, messages.GetArrayLength());
        Assert.Contains("Ambos ingresos son préstamos.", messages[1].GetProperty("content").GetString());
        Assert.Equal("¿No deberían ser dos registros por separado?", messages[2].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Si_el_modelo_responde_en_texto_plano_no_lo_sustituye_por_un_error_generico()
    {
        var service = CreateService(new RecordingHandler(
            """{"content":[{"type":"text","text":"Sí. Son dos movimientos independientes: el préstamo y el reembolso."}]}"""));

        var result = await service.AskAsync(
            "¿Son dos movimientos?",
            Array.Empty<ImportChatMessage>(),
            Array.Empty<ImportChatRowContext>(),
            Array.Empty<ImportAiCandidate>(),
            CancellationToken.None);

        Assert.Equal("Sí. Son dos movimientos independientes: el préstamo y el reembolso.", result.Reply);
        Assert.Empty(result.Changes);
    }

    private static ImportChatService CreateService(RecordingHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Anthropic:ApiKey"] = "test-key"
            })
            .Build();
        return new ImportChatService(new HttpClient(handler), configuration, NullLogger<ImportChatService>.Instance);
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
