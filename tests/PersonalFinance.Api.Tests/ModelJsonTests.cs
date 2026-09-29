using PersonalFinance.Api.Features.Chat;
using System.Text.Json;
using Xunit;

namespace PersonalFinance.Api.Tests;

/// <summary>
/// El chat devolvía un error genérico cada vez que el modelo envolvía el JSON
/// en markdown, lo precedía de una frase o se quedaba sin tokens a mitad.
/// </summary>
public class ModelJsonTests
{
    [Fact]
    public void Extract_UnwrapsMarkdownFence()
    {
        var raw = "```json\n{\"reply\":\"hola\"}\n```";

        using var parsed = JsonDocument.Parse(ModelJson.Extract(raw));

        Assert.Equal("hola", parsed.RootElement.GetProperty("reply").GetString());
    }

    [Fact]
    public void Extract_IgnoresTextAroundTheObject()
    {
        var raw = "Claro, aquí tienes:\n{\"reply\":\"hola\",\"proposedActions\":[]}\nEspero que ayude.";

        using var parsed = JsonDocument.Parse(ModelJson.Extract(raw));

        Assert.Equal("hola", parsed.RootElement.GetProperty("reply").GetString());
        Assert.Equal(0, parsed.RootElement.GetProperty("proposedActions").GetArrayLength());
    }

    [Fact]
    public void Extract_ClosesTruncatedResponse()
    {
        var raw = "{\"reply\":\"Tu nómina sí está en el calendario, entra el día 25";

        using var parsed = JsonDocument.Parse(ModelJson.Extract(raw));

        Assert.StartsWith("Tu nómina sí está", parsed.RootElement.GetProperty("reply").GetString());
    }

    [Fact]
    public void Extract_ClosesTruncatedArrayAndDanglingProperty()
    {
        var raw = "{\"reply\":\"hola\",\"proposedActions\":[{\"type\":\"create_expense\",\"amount\":";

        using var parsed = JsonDocument.Parse(ModelJson.Extract(raw));

        Assert.Equal("hola", parsed.RootElement.GetProperty("reply").GetString());
        Assert.Equal(1, parsed.RootElement.GetProperty("proposedActions").GetArrayLength());
    }

    [Fact]
    public void Extract_KeepsBracesInsideStrings()
    {
        var raw = "{\"reply\":\"usa {esto} y \\\"comillas\\\"\"}";

        using var parsed = JsonDocument.Parse(ModelJson.Extract(raw));

        Assert.Equal("usa {esto} y \"comillas\"", parsed.RootElement.GetProperty("reply").GetString());
    }

    [Fact]
    public void PlainText_ReturnsModelTextWhenThereIsNoJson()
    {
        var raw = "```\nNo he podido calcularlo, reformula la pregunta.\n```";

        Assert.Equal("No he podido calcularlo, reformula la pregunta.", ModelJson.PlainText(raw));
    }
}
