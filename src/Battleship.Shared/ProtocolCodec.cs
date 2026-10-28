using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Battleship.Shared;

public sealed class ProtocolCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public string Serialize(ProtocolMessage message) =>
        JsonSerializer.Serialize(message, Options);

    public bool TryDeserialize(string text, out ProtocolMessage? message, out string error)
    {
        message = null;
        if (string.IsNullOrEmpty(text))
        {
            error = "Message is empty.";
            return false;
        }

        if (Encoding.UTF8.GetByteCount(text) > ProtocolInfo.MaxMessageBytes)
        {
            error = "Message exceeds the maximum size.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !HasCanonicalEnums(document.RootElement))
            {
                error = "Message is not valid protocol JSON.";
                return false;
            }

            message = JsonSerializer.Deserialize<ProtocolMessage>(text, Options);
        }
        catch (JsonException)
        {
            error = "Message is not valid protocol JSON.";
            return false;
        }
        catch (ArgumentException)
        {
            error = "Message is not valid protocol JSON.";
            return false;
        }
        catch (NotSupportedException)
        {
            error = "Message contains an unsupported value.";
            return false;
        }

        if (message is null)
        {
            error = "Message is empty.";
            return false;
        }

        return Validate(message, out error);
    }

    public bool TryDeserializeUtf8(ReadOnlySpan<byte> bytes, out ProtocolMessage? message, out string error)
    {
        message = null;
        if (bytes.Length > ProtocolInfo.MaxMessageBytes)
        {
            error = "Message exceeds the maximum size.";
            return false;
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            error = "Message is not valid UTF-8 text.";
            return false;
        }

        return TryDeserialize(text, out message, out error);
    }

    public static bool Validate(ProtocolMessage message, out string error)
    {
        if (message.V is null)
        {
            error = "Missing v.";
            return false;
        }

        if (message.Type is null)
        {
            error = "Missing type.";
            return false;
        }

        return message.Type.Value switch
        {
            MessageType.HELLO or MessageType.READY or MessageType.QUIT => ValidateEmpty(message, out error),
            MessageType.SHOT => ValidateShot(message, out error),
            MessageType.RESULT => ValidateResult(message, out error),
            MessageType.CHAT => ValidateChat(message, out error),
            MessageType.ERROR => ValidateError(message, out error),
            _ => Fail("Unknown message type.", out error)
        };
    }

    private static bool ValidateEmpty(ProtocolMessage message, out string error)
    {
        if (HasAnyPayload(message) || message.Seq is not null)
        {
            return Fail("Message has fields that are not allowed for this type.", out error);
        }

        error = string.Empty;
        return true;
    }

    private static bool ValidateShot(ProtocolMessage message, out string error)
    {
        if (message.Seq is null or < 1)
        {
            return Fail("seq must be a positive integer.", out error);
        }

        if (!ProtocolInfo.IsValidCoordinate(message.Coord))
        {
            return Fail("coord must match A1..J10.", out error);
        }

        if (message.Outcome is not null || message.Ship is not null || message.Text is not null ||
            message.Code is not null || message.Detail is not null)
        {
            return Fail("SHOT contains fields that are not allowed for this type.", out error);
        }

        error = string.Empty;
        return true;
    }

    private static bool ValidateResult(ProtocolMessage message, out string error)
    {
        if (message.Seq is null or < 1)
        {
            return Fail("seq must be a positive integer.", out error);
        }

        if (message.Outcome is null)
        {
            return Fail("outcome is required.", out error);
        }

        var requiresShip = message.Outcome is ShotOutcome.SUNK or ShotOutcome.GAME_OVER;
        if (requiresShip && message.Ship is null)
        {
            return Fail("ship is required for SUNK and GAME_OVER.", out error);
        }

        if (!requiresShip && message.Ship is not null)
        {
            return Fail("ship is only allowed for SUNK and GAME_OVER.", out error);
        }

        if (message.Coord is not null || message.Text is not null || message.Code is not null ||
            message.Detail is not null)
        {
            return Fail("RESULT contains fields that are not allowed for this type.", out error);
        }

        error = string.Empty;
        return true;
    }

    private static bool ValidateChat(ProtocolMessage message, out string error)
    {
        if (!ProtocolInfo.IsValidChatText(message.Text))
        {
            return Fail("text must be 1..200 characters without control or bidi override characters.", out error);
        }

        if (message.Seq is not null || message.Coord is not null || message.Outcome is not null ||
            message.Ship is not null || message.Code is not null || message.Detail is not null)
        {
            return Fail("CHAT contains fields that are not allowed for this type.", out error);
        }

        error = string.Empty;
        return true;
    }

    private static bool ValidateError(ProtocolMessage message, out string error)
    {
        if (message.Code is null)
        {
            return Fail("code is required.", out error);
        }

        if (message.Detail is not null
            && (message.Detail.Length > ProtocolInfo.ErrorDetailMaxLength
                || ProtocolInfo.HasInvalidSurrogates(message.Detail)))
        {
            return Fail("detail must be at most 200 characters of valid text.", out error);
        }

        if (message.Seq is not null || message.Coord is not null || message.Outcome is not null ||
            message.Ship is not null || message.Text is not null)
        {
            return Fail("ERROR contains fields that are not allowed for this type.", out error);
        }

        error = string.Empty;
        return true;
    }

    private static bool HasCanonicalEnums(JsonElement root) =>
        IsCanonicalEnum<MessageType>(root, "type")
        && IsCanonicalEnum<ShotOutcome>(root, "outcome")
        && IsCanonicalEnum<ShipKind>(root, "ship")
        && IsCanonicalEnum<ProtocolErrorCode>(root, "code");

    private static bool IsCanonicalEnum<T>(JsonElement root, string propertyName)
        where T : struct, Enum
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return true;
        }

        var text = value.GetString();
        return text is not null && Enum.GetNames<T>().Contains(text, StringComparer.Ordinal);
    }

    private static bool HasAnyPayload(ProtocolMessage message) =>
        message.Coord is not null || message.Outcome is not null || message.Ship is not null ||
        message.Text is not null || message.Code is not null || message.Detail is not null;

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
