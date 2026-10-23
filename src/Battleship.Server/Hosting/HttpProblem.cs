using System.Text.Json;

namespace Battleship.Server.Hosting;

public static class HttpProblem
{
    public static async Task WriteAsync(HttpContext context, int status, string title, string detail)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        var payload = JsonSerializer.Serialize(new
        {
            type = "about:blank",
            title,
            status,
            detail
        });
        await context.Response.WriteAsync(payload);
    }
}
