using System.Net;
using System.Text;

namespace Battleship.Web.Tests.Infrastructure;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        _handler = handler;

    public List<HttpRequestMessage> Requests { get; } = new();

    public int CallCount { get; private set; }

    public static StubHttpMessageHandler Json(HttpStatusCode status, string body, string? reason = null) =>
        new((_, _) => Task.FromResult(Response(status, body, reason)));

    public static StubHttpMessageHandler Status(HttpStatusCode status, string? reason = null, string? body = null) =>
        new((_, _) => Task.FromResult(Response(status, body, reason)));

    public static StubHttpMessageHandler Throws(Exception exception) =>
        new((_, _) => Task.FromException<HttpResponseMessage>(exception));

    public static HttpResponseMessage Response(HttpStatusCode status, string? body, string? reason = null) =>
        new(status)
        {
            ReasonPhrase = reason,
            Content = new StringContent(body ?? string.Empty, Encoding.UTF8, "application/json")
        };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        lock (Requests)
        {
            Requests.Add(request);
        }

        return _handler(request, cancellationToken);
    }
}
