using System.Text.RegularExpressions;
using Battleship.Web;
using Battleship.Web.Pages;
using Battleship.Web.Tests.Infrastructure;
using Xunit;
using IndexPage = Battleship.Web.Pages.Index;

namespace Battleship.Web.Tests;

[Collection("real-server")]
public sealed class HomeUiTests
{
    private readonly RealServerFixture _fixture;

    public HomeUiTests(RealServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Home_LoadsWithTitleMetaAndJsonLd()
    {
        using var environment = new TestEnvironment();
        var page = new IndexPage();
        using var harness = await ComponentHarness.MountAsync(page, environment.Services);

        Assert.Contains("Sink the fleet. Keep your waters secret.", harness.Html, StringComparison.Ordinal);
        Assert.Contains("hero-shell", harness.Html, StringComparison.Ordinal);
        Assert.Contains("Single player", harness.Html, StringComparison.Ordinal);

        var jsonLd = (string)TestComponent.Get(page, "_jsonLd")!;
        Assert.Contains("\"WebSite\"", jsonLd, StringComparison.Ordinal);
        Assert.Contains("\"WebApplication\"", jsonLd, StringComparison.Ordinal);
        Assert.Contains("GameApplication", jsonLd, StringComparison.Ordinal);
        Assert.Contains("https://example.test/#website", jsonLd, StringComparison.Ordinal);

        var seo = new SeoMeta
        {
            Title = (string)UiAssert.StaticField(typeof(IndexPage), "PageTitleText")!,
            Description = (string)UiAssert.StaticField(typeof(IndexPage), "PageDescription")!,
            Path = "/"
        };
        TestComponent.Inject(seo, environment.Services);
        Assert.Equal("https://example.test/", TestComponent.Get(seo, "CanonicalUrl"));
        Assert.Contains("index, follow", (string)TestComponent.Get(seo, "RobotsValue")!, StringComparison.Ordinal);

        var source = await File.ReadAllTextAsync(RepoPaths.Web("Pages", "Index.razor"));
        Assert.Contains("<SeoMeta", source, StringComparison.Ordinal);
        Assert.Contains("Title=\"@PageTitleText\"", source, StringComparison.Ordinal);
        Assert.Contains("Description=\"@PageDescription\"", source, StringComparison.Ordinal);
        Assert.Contains("JsonLd=\"@_jsonLd\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_NoConsoleErrorsOrCspViolations()
    {
        using var environment = new TestEnvironment();
        var page = new IndexPage();
        using var harness = await ComponentHarness.MountAsync(page, environment.Services);

        Assert.Null(harness.Renderer.LastException);
        Assert.Empty(environment.Js.Calls);

        var index = await File.ReadAllTextAsync(RepoPaths.Web("wwwroot", "index.html"));
        Assert.False(Regex.IsMatch(index, @"\son[a-z]+\s*=", RegexOptions.IgnoreCase));
        Assert.DoesNotContain("javascript:", index, StringComparison.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(index, "<script(?<attrs>[^>]*)>(?<body>.*?)</script>", RegexOptions.Singleline))
        {
            Assert.Contains("src=", match.Groups["attrs"].Value, StringComparison.Ordinal);
            Assert.Equal(string.Empty, match.Groups["body"].Value.Trim());
        }

        Assert.DoesNotContain("eval(", index, StringComparison.Ordinal);
        Assert.Contains("blazor.webassembly.js", index, StringComparison.Ordinal);
        Assert.Contains("js/app.js", index, StringComparison.Ordinal);

        var script = await File.ReadAllTextAsync(RepoPaths.AppJs);
        Assert.Contains("\"use strict\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("eval(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("new Function(", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_SinglePlayerAndMultiplayerModesSelectable()
    {
        using var environment = new TestEnvironment();
        var page = new IndexPage();
        using var harness = await ComponentHarness.MountAsync(page, environment.Services);
        var html = harness.Html;

        Assert.Contains("Single player", html, StringComparison.Ordinal);
        Assert.Contains("Host a game", html, StringComparison.Ordinal);
        Assert.Contains("How to play", html, StringComparison.Ordinal);
        Assert.Contains("id=\"room-code\"", html, StringComparison.Ordinal);
        Assert.Contains(">Join</button>", html, StringComparison.Ordinal);

        TestComponent.Call(page, "GoSingle");
        TestComponent.Call(page, "GoHost");
        TestComponent.Call(page, "GoHelp");
        Assert.Equal(
            new[] { "https://example.test/single", "https://example.test/host", "https://example.test/help" },
            environment.Navigation.Records.Select(record => record.Uri).ToArray());

        TestComponent.Set(page, "RoomCodeInput", "abc234");
        TestComponent.Call(page, "Join");
        Assert.Equal("https://example.test/join/ABC234", environment.Navigation.Records[^1].Uri);

        TestComponent.Set(page, "RoomCodeInput", "abc");
        TestComponent.Call(page, "Join");
        Assert.Contains("exactly 6", (string)TestComponent.Get(page, "_error")!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeepLinkRefresh_LoadsApp()
    {
        using var http = _fixture.Server.CreateClient();
        foreach (var path in new[] { "join/ABC234", "single/hard", "help" })
        {
            using var response = await http.GetAsync(path);
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("<base href=\"/\" />", html, StringComparison.Ordinal);
            Assert.Contains("id=\"app\"", html, StringComparison.Ordinal);
            Assert.Contains("_framework/blazor.webassembly.js", html, StringComparison.Ordinal);
        }
    }
}

public sealed class RoutingUiTests
{
    [Fact]
    public async Task UnknownRoute_RedirectsToMenuWithoutHistoryEntry()
    {
        using var environment = new TestEnvironment();
        var page = new NotFoundRedirect();
        using var harness = await ComponentHarness.MountAsync(page, environment.Services);

        var record = Assert.Single(environment.Navigation.Records);
        Assert.Equal("https://example.test/", record.Uri);
        Assert.True(record.Replace);
        Assert.False(record.ForceLoad);
    }

    [Fact]
    public async Task SingleInvalidDifficulty_RedirectsToMedium()
    {
        using var environment = new TestEnvironment();
        var page = new SinglePlayerProbe { Difficulty = "impossible", RunParametersHook = true };
        using var harness = await ComponentHarness.MountAsync(page, environment.Services);

        var record = Assert.Single(environment.Navigation.Records);
        Assert.Equal("https://example.test/single/medium", record.Uri);
        Assert.True(record.Replace);
    }
}

public sealed class HelpUiTests
{
    [Fact]
    public async Task Help_ShowsRulesControlsDifficultiesAndMultiplayer()
    {
        using var environment = new TestEnvironment();
        var page = new Help();
        using var harness = await ComponentHarness.MountAsync(page, environment.Services);
        var html = harness.Html;

        Assert.Contains("How to play", html, StringComparison.Ordinal);
        Assert.Contains("Rules", html, StringComparison.Ordinal);
        Assert.Contains("10×10 board", html, StringComparison.Ordinal);
        Assert.Contains("Carrier (5)", html, StringComparison.Ordinal);
        Assert.Contains("Controls", html, StringComparison.Ordinal);
        Assert.Contains("Keyboard", html, StringComparison.Ordinal);
        Assert.Contains("Easy", html, StringComparison.Ordinal);
        Assert.Contains("Medium", html, StringComparison.Ordinal);
        Assert.Contains("Hard", html, StringComparison.Ordinal);
        Assert.Contains("Multiplayer", html, StringComparison.Ordinal);
        Assert.Contains("host fires first", html, StringComparison.Ordinal);

        var questions = UiAssert.StaticQuestions(typeof(Help), "Faqs");
        var jsonLd = (string)UiAssert.StaticField(typeof(Help), "FaqJsonLd")!;
        Assert.Contains("FAQPage", jsonLd, StringComparison.Ordinal);
        Assert.Equal(9, questions.Count);
        Assert.All(questions, question =>
        {
            Assert.Contains(question, html, StringComparison.Ordinal);
            Assert.Contains(question, jsonLd, StringComparison.Ordinal);
        });
    }
}
