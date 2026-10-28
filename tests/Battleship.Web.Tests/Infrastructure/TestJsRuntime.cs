using Microsoft.JSInterop;

namespace Battleship.Web.Tests.Infrastructure;

internal sealed class TestJsRuntime : IJSRuntime
{
    public List<(string Identifier, object?[] Arguments)> Calls { get; } = new();

    public Exception? ExceptionToThrow { get; set; }

    public Func<string, object?[], object?>? ResultFactory { get; set; }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        var values = args ?? Array.Empty<object?>();
        Calls.Add((identifier, values));
        if (ExceptionToThrow is not null)
        {
            return ValueTask.FromException<TValue>(ExceptionToThrow);
        }

        var result = ResultFactory?.Invoke(identifier, values);
        return ValueTask.FromResult(result is TValue typed ? typed : default!);
    }
}
