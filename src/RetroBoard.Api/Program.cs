using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR;
using RetroBoard.Api.Endpoints;
using RetroBoard.Api.Hubs;
using RetroBoard.Api.Services;
using RetroBoard.Contracts.Serialization;
using RetroBoard.Domain.Boards;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IBoardRepository, InMemoryBoardRepository>();
builder.Services.AddSingleton<IParticipantTracker, ParticipantTracker>();

builder.Services
    .AddSignalR(options => options.AddFilter<ExceptionHubFilter>())
    .AddJsonProtocol(options => options.PayloadSerializerOptions = RetroBoardJsonContext.CreateOptions());

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, RetroBoardJsonContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

var app = builder.Build();

app.UseResponseCompression();

if (app.Environment.IsDevelopment())
{
    // An F5/`dotnet run` debug session should never show stale bytes after a rebuild.
    // app.css, index.html, and the Blazor boot manifest have no cache-busting fingerprint of their
    // own, so the browser's default heuristic caching (no explicit Cache-Control -> guess a
    // freshness lifetime from Last-Modified) can and does serve an old copy across an
    // edit-rebuild-refresh cycle. Blanket no-cache sidesteps that entirely while debugging;
    // production keeps the long-cache-for-hashed-files strategy below untouched.
    app.Use((context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";
            return Task.CompletedTask;
        });

        return next();
    });
}
else
{
    // Most _framework/* filenames are content-hashed by the Blazor build, so they can be cached
    // indefinitely -- except the two loader scripts (blazor.webassembly.js, dotnet.js), which keep
    // their fixed names and so must NOT be cached forever, or a returning visitor could run a stale
    // loader against a new deploy's fingerprinted assembly set. Everything outside _framework/
    // (index.html, css, ...) keeps the framework's own short/no-cache default. Set from OnStarting
    // (not inline here) because the static file middleware downstream sets its own Cache-Control
    // just before writing the response -- setting it earlier in the pipeline would just get
    // overwritten.
    string[] unfingerprintedFrameworkFiles = ["/_framework/blazor.webassembly.js", "/_framework/dotnet.js"];
    app.Use((context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/_framework") &&
            !unfingerprintedFrameworkFiles.Contains(context.Request.Path.Value, StringComparer.OrdinalIgnoreCase))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                return Task.CompletedTask;
            });
        }

        return next();
    });
}

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapBoardEndpoints();
app.MapHub<BoardHub>("/hubs/board");
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program
{
}
