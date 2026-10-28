using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Battleship.Web.Net;

public sealed class RoomApiClient
{
    private readonly HttpClient _http;

    public RoomApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<ApiResult<string>> CreateRoomAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.PostAsync("api/rooms", null, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Created)
            {
                try
                {
                    var created = await response.Content.ReadFromJsonAsync<CreateRoomResponse>(cancellationToken: cancellationToken);
                    return created?.RoomCode is { Length: > 0 } code
                        ? ApiResult<string>.Ok(code)
                        : ApiResult<string>.Fail("INTERNAL", "The server returned an empty room code.");
                }
                catch (JsonException)
                {
                    return ApiResult<string>.Fail("INTERNAL", "The server returned an invalid response.");
                }
            }

            var detail = await ReadDetailAsync(response, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.BadRequest => ApiResult<string>.Fail("VALIDATION_FAILED", detail),
                HttpStatusCode.Conflict => ApiResult<string>.Fail("CONFLICT", detail),
                HttpStatusCode.TooManyRequests => ApiResult<string>.Fail("RATE_LIMITED", detail),
                _ => ApiResult<string>.Fail("UPSTREAM_UNAVAILABLE", detail)
            };
        }
        catch (OperationCanceledException)
        {
            return ApiResult<string>.Fail("UPSTREAM_UNAVAILABLE", "The server did not respond in time.");
        }
        catch (HttpRequestException)
        {
            return ApiResult<string>.Fail("UPSTREAM_UNAVAILABLE", "The server could not be reached.");
        }
    }

    public async Task<ApiResult<int>> GetRoomAsync(string roomCode, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync($"api/rooms/{Uri.EscapeDataString(roomCode)}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                try
                {
                    var state = await response.Content.ReadFromJsonAsync<RoomStateResponse>(cancellationToken: cancellationToken);
                    return state is null
                        ? ApiResult<int>.Fail("INTERNAL", "The server returned an empty room state.")
                        : ApiResult<int>.Ok(state.Participants);
                }
                catch (JsonException)
                {
                    return ApiResult<int>.Fail("INTERNAL", "The server returned an invalid response.");
                }
            }

            var detail = await ReadDetailAsync(response, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.BadRequest => ApiResult<int>.Fail("VALIDATION_FAILED", detail),
                HttpStatusCode.NotFound => ApiResult<int>.Fail("NOT_FOUND", detail),
                HttpStatusCode.Conflict => ApiResult<int>.Fail("CONFLICT", detail),
                HttpStatusCode.TooManyRequests => ApiResult<int>.Fail("RATE_LIMITED", detail),
                _ => ApiResult<int>.Fail("UPSTREAM_UNAVAILABLE", detail)
            };
        }
        catch (OperationCanceledException)
        {
            return ApiResult<int>.Fail("UPSTREAM_UNAVAILABLE", "The server did not respond in time.");
        }
        catch (HttpRequestException)
        {
            return ApiResult<int>.Fail("UPSTREAM_UNAVAILABLE", "The server could not be reached.");
        }
    }

    private static async Task<string> ReadDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("detail", out var detail)
                && detail.ValueKind == JsonValueKind.String)
            {
                return detail.GetString() ?? response.ReasonPhrase ?? "Request failed.";
            }
        }
        catch (JsonException)
        {
            // Fall back to the status line.
        }
        catch (InvalidOperationException)
        {
            // No content.
        }

        return response.ReasonPhrase ?? "Request failed.";
    }

    private sealed record CreateRoomResponse(string RoomCode);

    private sealed record RoomStateResponse(int Participants);
}
