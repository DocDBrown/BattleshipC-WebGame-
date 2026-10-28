using Battleship.Web;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

WebComposition.Configure(builder.Services, builder.Configuration, new Uri(builder.HostEnvironment.BaseAddress));

await builder.Build().RunAsync();
