using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Battleship.Web.Tests.Infrastructure;

internal static class UiAssert
{
    private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static string ElementMarkup(string html, string innerText)
    {
        var index = html.IndexOf(innerText, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidOperationException($"'{innerText}' was not found in the rendered HTML.");
        }

        var open = html.LastIndexOf('<', index);
        var close = html.IndexOf('>', open);
        var nameStart = open + 1;
        while (nameStart < close && !char.IsLetterOrDigit(html[nameStart]))
        {
            nameStart++;
        }

        var nameEnd = nameStart;
        while (nameEnd < close && (char.IsLetterOrDigit(html[nameEnd]) || html[nameEnd] == '-'))
        {
            nameEnd++;
        }

        var tag = html[nameStart..nameEnd];
        var end = html.IndexOf($"</{tag}>", index, StringComparison.Ordinal);
        return end < 0 ? html[open..(close + 1)] : html[open..(end + tag.Length + 3)];
    }

    public static bool ElementHasAttribute(string html, string innerText, string attribute)
    {
        var markup = ElementMarkup(html, innerText);
        var close = markup.IndexOf('>', StringComparison.Ordinal);
        var tag = markup[..close];
        return tag.Contains($" {attribute}", StringComparison.Ordinal)
               || tag.Contains($" {attribute}=", StringComparison.Ordinal);
    }

    public static int Count(string html, string token) =>
        Regex.Matches(html, Regex.Escape(token), RegexOptions.CultureInvariant).Count;

    public static object? StaticField(Type type, string name) =>
        type.GetField(name, StaticFlags)?.GetValue(null);

    public static IReadOnlyList<string> StaticQuestions(Type type, string fieldName)
    {
        if (StaticField(type, fieldName) is not System.Collections.IEnumerable items)
        {
            throw new InvalidOperationException($"Static field {fieldName} was not an enumerable.");
        }

        var questions = new List<string>();
        foreach (var item in items)
        {
            var property = item!.GetType().GetProperty("Question", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            questions.Add((string)property!.GetValue(item)!);
        }

        return questions;
    }

    public static void AssertStandardButtonMarkup(string html)
    {
        foreach (Match match in Regex.Matches(html, "<button\\b[^>]*>", RegexOptions.CultureInvariant))
        {
            Assert.Contains("type=", match.Value, StringComparison.Ordinal);
        }
    }

    public static void AssertLabelledInputs(string html)
    {
        foreach (Match match in Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.CultureInvariant))
        {
            var tag = match.Value;
            var idMatch = Regex.Match(tag, "id=\"(?<id>[^\"]+)\"", RegexOptions.CultureInvariant);
            if (!idMatch.Success)
            {
                continue;
            }

            var id = idMatch.Groups["id"].Value;
            Assert.Contains($"for=\"{id}\"", html, StringComparison.Ordinal);
        }
    }

    public static void AssertCellMarkup(string html)
    {
        foreach (Match match in Regex.Matches(html, "<button\\b[^>]*role=\"gridcell\"[^>]*>", RegexOptions.CultureInvariant))
        {
            Assert.Contains("aria-label=", match.Value, StringComparison.Ordinal);
            Assert.Contains("data-coordinate=", match.Value, StringComparison.Ordinal);
        }
    }
}
