using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace RetroBoard.Tests.Integration.TestSupport;

/// <summary>Enables detailed hub errors so test failures show the real server-side exception
/// message. BoardHub's reconnect grace windows (<see cref="RetroBoard.Api.Extensions.ApiConstants"/>)
/// are fixed constants, not DI-overridable, so tests that exercise the "still gone after the grace
/// period" path (see <c>BoardHubDisconnectSweepTests</c>) genuinely wait out the production-sized 15
/// seconds.</summary>
public class RetroBoardWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.Configure<HubOptions>(options => options.EnableDetailedErrors = true);
        });
    }
}
