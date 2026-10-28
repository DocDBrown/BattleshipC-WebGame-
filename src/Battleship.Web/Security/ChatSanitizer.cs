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
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (char.IsHighSurrogate(ch))
            {
                if (index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                {
                    builder.Append(ch).Append(text[index + 1]);
                    index++;
                }

                continue;
            }

            if (char.IsLowSurrogate(ch))
            {
                continue;
            }

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
