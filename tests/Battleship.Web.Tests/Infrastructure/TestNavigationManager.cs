using Microsoft.AspNetCore.Components;

namespace Battleship.Web.Tests.Infrastructure;

internal sealed record NavigationRecord(string Uri, bool ForceLoad, bool Replace);

internal sealed class TestNavigationManager : NavigationManager
{
    public TestNavigationManager(string baseUri = "https://example.test/") => Initialize(baseUri, baseUri);

    public List<NavigationRecord> Records { get; } = new();

    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
        var absolute = ToAbsoluteUri(uri).ToString();
        Records.Add(new NavigationRecord(absolute, options.ForceLoad, options.ReplaceHistoryEntry));
        Uri = absolute;
    }
}
