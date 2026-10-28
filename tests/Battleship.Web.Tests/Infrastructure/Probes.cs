using Battleship.Web.Components;
using Battleship.Web.Pages;

namespace Battleship.Web.Tests.Infrastructure;

internal sealed class SinglePlayerProbe : SinglePlayer
{
    public bool RunParametersHook { get; set; }

    public Func<Random>? RandomFactory { get; set; }

    protected override void OnParametersSet()
    {
        if (RunParametersHook)
        {
            base.OnParametersSet();
        }
    }

    protected override Random CreateRandom() => RandomFactory?.Invoke() ?? base.CreateRandom();
}

internal sealed class MultiplayerGameProbe : MultiplayerGame
{
    public bool RunInitialize { get; set; }

    protected override Task OnInitializedAsync() =>
        RunInitialize ? base.OnInitializedAsync() : Task.CompletedTask;
}
