using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Battleship.Shared;
using Battleship.Web;
using Battleship.Web.Logging;
using Battleship.Web.Net;
using Battleship.Web.Security;
using Battleship.Web.Session;
using Battleship.Web.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Battleship.Web.Tests;

public sealed class ChatSanitizerTests
{
    [Fact]
    public void IsValid_DelegatesToProtocolInfo()
    {
        var samples = new string?[]
        {
            "hello",
            null,
            string.Empty,
            new string('a', 201),
            "safe\u202Etext",
            "😀",
            "line\nbreak"
        };
        foreach (var sample in samples)
        {
            Assert.Equal(ProtocolInfo.IsValidChatText(sample), ChatSanitizer.IsValid(sample));
        }
    }

    [Fact]
    public void Sanitize_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ChatSanitizer.Sanitize(null));
        Assert.Equal(string.Empty, ChatSanitizer.Sanitize(string.Empty));
    }

    [Fact]
    public void Sanitize_StripsControlCharacters()
    {
        Assert.Equal("hello world", ChatSanitizer.Sanitize("hel\u0000lo\u0007 world\n"));
    }

    [Fact]
    public void Sanitize_StripsBidiOverrides()
    {
        Assert.Equal("abcdef", ChatSanitizer.Sanitize("a\u202Eb\u2066c\u200Fd\u061Ce\u202Cf"));
    }

    [Fact]
    public void Sanitize_TrimsWhitespace()
    {
        Assert.Equal("message", ChatSanitizer.Sanitize("   message \t\r\n"));
    }

    [Fact]
    public void Sanitize_TruncatesTo200WithoutSplittingSurrogate()
    {
        var text = new string('a', 199) + "😀tail";
        var clean = ChatSanitizer.Sanitize(text);
        Assert.Equal(199, clean.Length);
        Assert.False(ProtocolInfo.HasInvalidSurrogates(clean));
        Assert.True(ProtocolInfo.IsValidChatText(clean));
    }

    [Fact]
    public void Sanitize_PreservesEmoji()
    {
        Assert.Equal("fire 🔥 now", ChatSanitizer.Sanitize(" fire 🔥 now "));
    }

    [Fact]
    public void Sanitize_LoneSurrogate_OutputPassesIsValid()
    {
        var clean = ChatSanitizer.Sanitize("safe\uD83Dtext\uDE00");
        Assert.Equal("safetext", clean);
        Assert.True(ProtocolInfo.IsValidChatText(clean));
    }

    [Fact]
    public void Sanitize_NonEmptyOutput_AlwaysPassesIsValid()
    {
        var samples = new[]
        {
            "plain",
            "  padded  ",
            "😀😀😀",
            new string('x', 400),
            "\u0001\u0002abc\u202E",
            "mixed 👩‍🚀 text",
            "a\uD800b",
            "a\uDC00b"
        };
        foreach (var sample in samples)
        {
            var clean = ChatSanitizer.Sanitize(sample);
            if (clean.Length > 0)
            {
                Assert.True(ProtocolInfo.IsValidChatText(clean), $"Failed for sample with length {sample.Length}.");
            }
        }
    }
}

public sealed class GameOptionsTests
{
    [Fact]
    public void Defaults_MatchDocumentedValues()
    {
        var options = GameOptions.FromConfiguration(Configuration());
        Assert.Null(options.AiSeed);
        Assert.Equal(600, options.AiMoveDelayMs);
        Assert.Equal(250, options.AiDecisionBudgetMs);
        Assert.Equal(10, options.HttpTimeoutSeconds);
        Assert.Equal(600, options.JoinTimeoutSeconds);
        Assert.Equal(15, options.HandshakeTimeoutSeconds);
        Assert.Equal(60, options.ReadTimeoutSeconds);
        Assert.Equal(20, options.JoinRetryCount);
        Assert.Equal(500, options.JoinRetryDelayMs);
    }

    [Fact]
    public void FromConfiguration_ReadsAllKeys()
    {
        var options = GameOptions.FromConfiguration(Configuration(new Dictionary<string, string?>
        {
            ["Battleship:AiSeed"] = "99",
            ["Battleship:AiMoveDelayMs"] = "5",
            ["Battleship:AiDecisionBudgetMs"] = "6",
            ["Battleship:HttpTimeoutSeconds"] = "7",
            ["Battleship:JoinTimeoutSeconds"] = "8",
            ["Battleship:HandshakeTimeoutSeconds"] = "9",
            ["Battleship:ReadTimeoutSeconds"] = "10",
            ["Battleship:JoinRetryCount"] = "11",
            ["Battleship:JoinRetryDelayMs"] = "12"
        }));
        Assert.Equal(99, options.AiSeed);
        Assert.Equal(5, options.AiMoveDelayMs);
        Assert.Equal(6, options.AiDecisionBudgetMs);
        Assert.Equal(7, options.HttpTimeoutSeconds);
        Assert.Equal(8, options.JoinTimeoutSeconds);
        Assert.Equal(9, options.HandshakeTimeoutSeconds);
        Assert.Equal(10, options.ReadTimeoutSeconds);
        Assert.Equal(11, options.JoinRetryCount);
        Assert.Equal(12, options.JoinRetryDelayMs);
    }

    [Fact]
    public void FromConfiguration_ClampsOutOfRangeValues()
    {
        var options = GameOptions.FromConfiguration(Configuration(new Dictionary<string, string?>
        {
            ["Battleship:AiMoveDelayMs"] = "-10",
            ["Battleship:AiDecisionBudgetMs"] = "0",
            ["Battleship:HttpTimeoutSeconds"] = "-1",
            ["Battleship:JoinTimeoutSeconds"] = "0",
            ["Battleship:HandshakeTimeoutSeconds"] = "-5",
            ["Battleship:ReadTimeoutSeconds"] = "0",
            ["Battleship:JoinRetryCount"] = "-7",
            ["Battleship:JoinRetryDelayMs"] = "-3"
        }));
        Assert.Equal(0, options.AiMoveDelayMs);
        Assert.Equal(1, options.AiDecisionBudgetMs);
        Assert.Equal(1, options.HttpTimeoutSeconds);
        Assert.Equal(1, options.JoinTimeoutSeconds);
        Assert.Equal(1, options.HandshakeTimeoutSeconds);
        Assert.Equal(1, options.ReadTimeoutSeconds);
        Assert.Equal(0, options.JoinRetryCount);
        Assert.Equal(0, options.JoinRetryDelayMs);
    }

    [Fact]
    public void FromConfiguration_NonNumeric_FallsBackToDefault()
    {
        var options = GameOptions.FromConfiguration(Configuration(new Dictionary<string, string?>
        {
            ["Battleship:AiMoveDelayMs"] = "soon",
            ["Battleship:HttpTimeoutSeconds"] = "ten"
        }));
        Assert.Equal(600, options.AiMoveDelayMs);
        Assert.Equal(10, options.HttpTimeoutSeconds);
    }

    [Fact]
    public void FromConfiguration_AiSeedAbsent_IsNull()
    {
        Assert.Null(GameOptions.FromConfiguration(Configuration()).AiSeed);
        Assert.Null(GameOptions.FromConfiguration(Configuration(new Dictionary<string, string?>
        {
            ["Battleship:AiSeed"] = "not-a-number"
        })).AiSeed);
    }

    [Fact]
    public void FromConfiguration_IsCultureInvariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var options = GameOptions.FromConfiguration(Configuration(new Dictionary<string, string?>
            {
                ["Battleship:AiMoveDelayMs"] = "1,5",
                ["Battleship:HttpTimeoutSeconds"] = "7"
            }));
            Assert.Equal(600, options.AiMoveDelayMs);
            Assert.Equal(7, options.HttpTimeoutSeconds);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static IConfiguration Configuration(IDictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values ?? new Dictionary<string, string?>()).Build();
}

public sealed class ApiResultTests
{
    [Fact]
    public void Ok_SetsSuccessAndValue()
    {
        var result = ApiResult<string>.Ok("value");
        Assert.True(result.Success);
        Assert.Equal("value", result.Value);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.Detail);
    }

    [Fact]
    public void Fail_SetsCodeAndDetail()
    {
        var result = ApiResult<int>.Fail("NOT_FOUND", "missing");
        Assert.False(result.Success);
        Assert.Equal(0, result.Value);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
        Assert.Equal("missing", result.Detail);
    }
}

public sealed class WsFrameTests
{
    [Fact]
    public void Factories_SetKindAndFields()
    {
        var text = WsFrame.FromText("hello");
        Assert.Equal(WsFrameKind.Text, text.Kind);
        Assert.Equal("hello", text.Text);
        Assert.Null(text.CloseStatus);
        Assert.Null(text.CloseDescription);

        var binary = WsFrame.Binary();
        Assert.Equal(WsFrameKind.Binary, binary.Kind);
        Assert.Null(binary.Text);

        var large = WsFrame.TooLarge();
        Assert.Equal(WsFrameKind.TooLarge, large.Kind);

        var close = WsFrame.Close((System.Net.WebSockets.WebSocketCloseStatus)4001, "left");
        Assert.Equal(WsFrameKind.Close, close.Kind);
        Assert.Equal((System.Net.WebSockets.WebSocketCloseStatus)4001, close.CloseStatus);
        Assert.Equal("left", close.CloseDescription);
    }
}

public sealed class SeoInfoTests
{
    [Fact]
    public void Absolute_CombinesBaseAndPath()
    {
        Assert.Equal("https://example.test/help", SeoInfo.Absolute("https://example.test/", "/help"));
        Assert.Equal("https://example.test/play/single", SeoInfo.Absolute("https://example.test/", "play/single"));
    }

    [Fact]
    public void Absolute_TrimsLeadingSlash()
    {
        Assert.Equal("https://example.test/app/", SeoInfo.Absolute("https://example.test/app/", "/"));
        Assert.Equal("https://example.test/app/help", SeoInfo.Absolute("https://example.test/app/", "/help"));
    }

    [Fact]
    public void ToJsonLd_EscapesScriptClosingTag()
    {
        var json = SeoInfo.ToJsonLd(new JsonObject { ["name"] = "</script><img src=x>" });
        Assert.Contains("\\u003C/script", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("</script", json, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(JsonNode.Parse(json));
    }
}

public sealed class MultiplayerSessionTests
{
    [Fact]
    public void Defaults_NextSeqOneNoPendingNoWinner()
    {
        var session = new MultiplayerSession();
        Assert.Equal(1, session.NextSeq);
        Assert.Null(session.PendingSeq);
        Assert.Null(session.PendingCoordinate);
        Assert.Null(session.Winner);
        Assert.Equal(Side.Peer, session.Turn);
        Assert.False(session.HandshakeComplete);
        Assert.Empty(session.Chat);
        Assert.Empty(session.EnemySunk);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleCollection
{
    public const string Name = "Console";
}

[Collection(ConsoleCollection.Name)]
public sealed class AppLoggerTests
{
    [Fact]
    public void Log_WithoutDetail_FormatsEventOnly()
    {
        Assert.Equal("[battleship] game_started", Capture(() => AppLogger.Log("game_started")));
    }

    [Fact]
    public void Log_WithDetail_FormatsEventAndDetail()
    {
        Assert.Equal("[battleship] ship_sunk CARRIER", Capture(() => AppLogger.Log("ship_sunk", "CARRIER")));
    }

    private static string Capture(Action action)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            action();
        }
        finally
        {
            Console.SetOut(original);
        }

        return writer.ToString().TrimEnd('\r', '\n');
    }
}

public sealed class AppJsTests
{
    private static readonly string Script = File.ReadAllText(RepoPaths.AppJs);

    [Fact]
    public void FocusById_MissingElement_NoThrow() =>
        Assert.Matches(@"if \(!element\) \{\s*return;\s*\}", Script);

    [Fact]
    public void FocusById_UsesPreventScroll() =>
        Assert.Contains("element.focus({ preventScroll: true });", Script, StringComparison.Ordinal);

    [Fact]
    public void FocusById_OptionsUnsupported_FallsBack() =>
        Assert.Matches(@"catch \(error\) \{\s*element\.focus\(\);\s*\}", Script);

    [Fact]
    public void CopyText_SecureContext_UsesClipboard()
    {
        Assert.Contains("navigator.clipboard && window.isSecureContext", Script, StringComparison.Ordinal);
        Assert.Contains("navigator.clipboard.writeText(text)", Script, StringComparison.Ordinal);
    }

    [Fact]
    public void CopyText_ClipboardRejects_FallsBackToLegacy() =>
        Assert.Matches(@"\.catch\(function \(\) \{\s*return legacyCopy\(text\);\s*\}\)", Script);

    [Fact]
    public void CopyText_InsecureContext_UsesLegacy() =>
        Assert.Contains("Promise.resolve(legacyCopy(text))", Script, StringComparison.Ordinal);

    [Fact]
    public void LegacyCopy_AlwaysRemovesTextarea() =>
        Assert.Matches(@"finally \{\s*document\.body\.removeChild\(area\);\s*\}", Script);

    [Fact]
    public void LegacyCopy_ExecCommandThrows_ReturnsFalse()
    {
        Assert.Contains("copied = document.execCommand(\"copy\");", Script, StringComparison.Ordinal);
        Assert.Matches(@"catch \(error\) \{\s*copied = false;\s*\}", Script);
        Assert.Contains("return copied;", Script, StringComparison.Ordinal);
    }

    [Fact]
    public void Dragstart_ShipCard_SetsDataAndMoveEffect()
    {
        Assert.Contains("target.closest(\"[data-ship]\")", Script, StringComparison.Ordinal);
        Assert.Contains("dataTransfer.setData(\"text/plain\"", Script, StringComparison.Ordinal);
        Assert.Contains("dataTransfer.effectAllowed = \"move\"", Script, StringComparison.Ordinal);
    }

    [Fact]
    public void Dragstart_NonShipTarget_Ignored() =>
        Assert.Contains("if (!card || !event.dataTransfer)", Script, StringComparison.Ordinal);

    [Fact]
    public void Dragstart_NoDataTransfer_Ignored() =>
        Assert.Contains("!event.dataTransfer", Script, StringComparison.Ordinal);

    [Fact]
    public void Script_ExposesOnlyWindowBattleshipGlobal()
    {
        var assignments = Regex.Matches(Script, @"window\.\w+\s*=");
        var assignment = Assert.Single(assignments.Cast<Match>());
        Assert.Contains("window.battleship", assignment.Value, StringComparison.Ordinal);
    }
}
