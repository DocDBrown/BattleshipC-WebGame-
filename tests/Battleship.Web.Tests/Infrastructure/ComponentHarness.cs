using Microsoft.AspNetCore.Components;

namespace Battleship.Web.Tests.Infrastructure;

internal sealed class ComponentHarness : IDisposable
{
    private ComponentHarness(TestRenderer renderer, int hostId, IComponent component)
    {
        Renderer = renderer;
        HostId = hostId;
        Component = component;
    }

    public TestRenderer Renderer { get; }
    public int HostId { get; }
    public IComponent Component { get; }

    public string Html => Renderer.GetHtml(HostId);

    public static async Task<ComponentHarness> MountAsync(IComponent component, IServiceProvider services)
    {
        TestComponent.Inject(component, services);
        var renderer = new TestRenderer(services);
        var hostId = renderer.Attach(component);
        await renderer.RenderAsync(hostId);
        return new ComponentHarness(renderer, hostId, component);
    }

    public object? Get(string name) => TestComponent.Get(Component, name);

    public void Set(string name, object? value) => TestComponent.Set(Component, name, value);

    public object? Call(string name, params object?[] arguments) => TestComponent.Call(Component, name, arguments);

    public Task CallAsync(string name, params object?[] arguments) => TestComponent.CallAsync(Component, name, arguments);

    public void SetPhase(string field, string value) => TestComponent.SetEnum(Component, field, value);

    public void Refresh() => TestComponent.Call(Component, "StateHasChanged");

    public void Dispose() => Renderer.Dispose();
}
