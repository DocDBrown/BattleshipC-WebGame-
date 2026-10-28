using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Battleship.Server.Tests;

public sealed class HttpProblemTests
{
    private static async Task<(DefaultHttpContext Context, string Body)> WriteAsync(int status, string detail)
    {
        var context = TestData.HttpContext();
        await Battleship.Server.Hosting.HttpProblem.WriteAsync(context, status, "Title", detail);
        return (context, TestData.Body(context));
    }

    [Fact]
    public async Task WriteAsync_SetsStatusCode()
    {
        var (context, _) = await WriteAsync(418, "tea");
        Assert.Equal(418, context.Response.StatusCode);
    }

    [Fact]
    public async Task WriteAsync_SetsProblemJsonContentType()
    {
        var (context, _) = await WriteAsync(400, "bad");
        Assert.Equal("application/problem+json", context.Response.ContentType);
    }

    [Fact]
    public async Task WriteAsync_BodyHasTypeTitleStatusDetail()
    {
        var (_, body) = await WriteAsync(429, "slow");
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal("about:blank", root.GetProperty("type").GetString());
        Assert.Equal("Title", root.GetProperty("title").GetString());
        Assert.Equal(429, root.GetProperty("status").GetInt32());
        Assert.Equal("slow", root.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task WriteAsync_DetailIsJsonEscaped()
    {
        var (_, body) = await WriteAsync(400, "quote \" and \n newline");
        using var document = JsonDocument.Parse(body);
        Assert.Equal("quote \" and \n newline", document.RootElement.GetProperty("detail").GetString());
    }
}
