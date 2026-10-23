using System.Text;
using Battleship.Shared;

namespace Battleship.Web.Security;

public static class ChatSanitizer
{
    public static bool IsValid(string? text) => ProtocolInfo.IsValidChatText(text);

    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsControl(ch) || ProtocolInfo.IsBidiOverride(ch))
            {
                continue;
            }

            builder.Append(ch);
        }

        var clean = builder.ToString().Trim();
        if (clean.Length <= ProtocolInfo.ChatMaxLength)
        {
            return clean;
        }

        var length = ProtocolInfo.ChatMaxLength;
        if (char.IsHighSurrogate(clean[length - 1]))
        {
            length--;
        }

        return clean[..length];
    }
}
