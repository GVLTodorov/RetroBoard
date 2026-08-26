using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RetroBoard.Client;
using RetroBoard.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<BoardApiClient>();
builder.Services.AddScoped<ParticipantSessionState>();
builder.Services.AddScoped<IBoardHubClientFactory, BoardHubClientFactory>();

// Coverage note: WebAssemblyHostBuilder.CreateDefault() itself throws PlatformNotSupportedException
// outside a real browser WASM host (it eagerly calls into System.Runtime.InteropServices.JavaScript
// to read the page's base URI), so nothing in this file can execute inside a dotnet test process.
// Each registered service (BoardApiClient, ParticipantSessionState, BoardHubClientFactory) is still
// fully unit tested on its own elsewhere; only this file's own DI-wiring lines are the accepted
// exception -- same posture PlanningPoker's own Program.cs documents.
await builder.Build().RunAsync();
