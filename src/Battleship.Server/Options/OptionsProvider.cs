using Microsoft.Extensions.Options;

namespace Battleship.Server.Options;

public sealed class OptionsProvider
{
    private readonly IOptionsMonitor<BattleshipOptions> _options;

    public OptionsProvider(IOptionsMonitor<BattleshipOptions> options)
    {
        _options = options;
    }

    public BattleshipOptions Get() => _options.CurrentValue.Normalized();
}
