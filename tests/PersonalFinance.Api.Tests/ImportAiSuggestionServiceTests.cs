using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PersonalFinance.Api.Features.Ledger.Common;

namespace PersonalFinance.Api.Tests;

public sealed class ImportAiSuggestionServiceTests
{
    [Fact]
    public async Task Lee_la_respuesta_camel_case_de_anthropic()
    {
        var conceptId = Guid.NewGuid();
        var anthropicResponse = $$"""
            {
              "content": [
                {
                  "type": "text",
                  "text": "[{\"description\":\"MERCADONA\",\"conceptId\":\"{{conceptId}}\",\"confidence\":0.95}]"
                }
              ]
            }
            """;
        var http = new HttpClient(new StubHttpMessageHandler(anthropicResponse));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Anthropic:ApiKey"] = "test-key"
            })
            .Build();
        var service = new ImportAiSuggestionService(
            http,
            configuration,
            NullLogger<ImportAiSuggestionService>.Instance);

        var result = await service.SuggestAsync(
            new[] { "MERCADONA" },
            new[] { new ImportAiCandidate(conceptId, "Alimentación", "Expense") },
            CancellationToken.None);

        var suggestion = Assert.Single(result);
        Assert.Equal("MERCADONA", suggestion.Key);
        Assert.Equal(conceptId, suggestion.Value.ConceptId);
        Assert.Equal(0.95m, suggestion.Value.Confidence);
        Assert.Null(service.FailureReason);
    }

    private sealed class StubHttpMessageHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
    }
}
