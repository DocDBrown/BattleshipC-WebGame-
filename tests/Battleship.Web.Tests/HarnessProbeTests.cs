using Battleship.Web.Pages;
using Battleship.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Battleship.Web.Tests;

public sealed class HarnessProbeTests
{
    [Fact]
    public async Task Harness_RendersIndexAndInjectsServices()
    {
        using var environment = new TestEnvironment();
        var page = new Battleship.Web.Pages.Index();
        var harness = await ComponentHarness.MountAsync(page, environment.Services);
        Assert.Null(harness.Renderer.LastException);
        Assert.Contains("Sink the fleet", harness.Html, StringComparison.Ordinal);
        Assert.IsType<TestNavigationManager>(TestComponent.Get(page, "Navigation"));
        harness.Dispose();
    }
}
