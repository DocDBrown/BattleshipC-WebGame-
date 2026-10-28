using Battleship.Web.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Battleship.Web;

public static class WebComposition
{
    public static void Configure(IServiceCollection services, IConfiguration configuration, Uri baseAddress)
    {
        var options = GameOptions.FromConfiguration(configuration);
        services.AddScoped(_ => new HttpClient
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds)
        });
        services.AddScoped<RoomApiClient>();
        services.AddTransient<WsTransport>();
    }
}
