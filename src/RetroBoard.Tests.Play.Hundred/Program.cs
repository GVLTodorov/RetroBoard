// Resource-usage load driver: spins up 10 boards x 10 participants (real SignalR hub connections,
// no browsers) and has them write cards like real people would -- each round's cards land at
// random moments within the round rather than all at once, and each board's round takes 15-20
// wall-clock seconds end to end. While that runs, it samples RetroBoard.Api's own CPU% and
// working-set memory at a fixed interval and renders the series as a dependency-free SVG line
// chart. The goal is purely to see what the app itself costs under sustained concurrent load, not
// to produce a recording -- no Playwright/browser involved, unlike RetroBoard.Tests.Play.
//
// Unlike PlanningPoker's Room (pick -> reveal -> reset, repeatable indefinitely), a Board's phase
// only ever moves forward once (Writing -> Voting -> ActionItems), so there's no reveal/reset step
// to loop here. AddCard during the Writing phase is itself the repeatable, sustained hot path
// (every card add fans out an individualized per-viewer snapshot to every connected participant --
// see BoardHub.BroadcastBoardStateAsync), so each round is simply every participant adding one more
// card at a staggered moment.
//
//   dotnet run --project RetroBoard.Tests.Play.Hundred -c Release -- \
//     http://localhost:6233 <api-pid> docs/hundred-resource-usage.svg 10 10 8

using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using RetroBoard.Contracts;
using RetroBoard.Contracts.Messages;
using RetroBoard.Contracts.Requests;
using RetroBoard.Contracts.Serialization;

var baseUrl = args.Length > 0 ? args[0] : "http://localhost:6233";
var apiPid = args.Length > 1 && int.TryParse(args[1], out var parsedPid) ? parsedPid : 0;
var outputSvgPath = args.Length > 2 ? args[2] : "docs/hundred-resource-usage.svg";
var boardCount = args.Length > 3 ? int.Parse(args[3]) : 10;
var participantsPerBoard = args.Length > 4 ? int.Parse(args[4]) : 10;
var rounds = args.Length > 5 ? int.Parse(args[5]) : 8;

Console.WriteLine(
    $"Resource load test: {boardCount} boards x {participantsPerBoard} participants x {rounds} rounds (15-20s each) against {baseUrl}");

var monitorCts = new CancellationTokenSource();
var monitorTask = apiPid > 0
    ? MonitorResourceUsageAsync(apiPid, TimeSpan.FromMilliseconds(500), monitorCts.Token)
    : Task.FromResult(new List<ResourceSample>());

if (apiPid <= 0)
{
    Console.WriteLine("No API process id given -- skipping CPU/memory sampling, running the load only.");
}

var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
var jsonOptions = RetroBoardJsonContext.CreateOptions();
var runId = Guid.NewGuid().ToString("N")[..8];
var overallStopwatch = Stopwatch.StartNew();

var boardTasks = Enumerable.Range(0, boardCount).Select(async boardIndex =>
{
    var createResponse = await httpClient.PostAsJsonAsync(
        "/api/boards",
        new CreateBoardRequest($"Hundred {runId} {boardIndex}", TemplateType.WentWellDidntWork, BlurUntilReveal: false, VoteBudget: null, MaxVotesPerCard: null),
        jsonOptions);
    var board = await createResponse.Content.ReadFromJsonAsync<BoardSummaryResponse>(jsonOptions)
        ?? throw new InvalidOperationException("Board creation failed during load test setup.");

    var connections = new List<HubConnection>();
    IReadOnlyList<Guid> columnIds = [];

    for (var p = 0; p < participantsPerBoard; p++)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}/hubs/board")
            .AddJsonProtocol(o => o.PayloadSerializerOptions = RetroBoardJsonContext.CreateOptions())
            .Build();

        await connection.StartAsync();
        var joined = await connection.InvokeAsync<JoinBoardResponse>(
            "JoinBoard", board.BoardId, $"Bot{boardIndex}-{p}", (Guid?)null);
        connections.Add(connection);

        if (p == 0)
        {
            columnIds = joined.State.Columns.Select(c => c.ColumnId).ToList();
        }
    }

    for (var round = 0; round < rounds; round++)
    {
        var roundStopwatch = Stopwatch.StartNew();
        var roundDurationSeconds = 15 + Random.Shared.NextDouble() * 5; // 15-20s, one card per participant per round

        // Real participants don't write in lockstep -- stagger each card add randomly across the
        // first 60% of the round, leaving the rest of the window to look like a pause between bursts.
        var writeTasks = connections.Select(async (connection, participantIndex) =>
        {
            var thinkTimeMs = (int)(Random.Shared.NextDouble() * roundDurationSeconds * 0.6 * 1000);
            await Task.Delay(thinkTimeMs);
            var columnId = columnIds[(participantIndex + round) % columnIds.Count];
            await connection.InvokeAsync("AddCard", columnId, $"Round {round} card {participantIndex}");
        });
        await Task.WhenAll(writeTasks);

        var remaining = roundDurationSeconds - roundStopwatch.Elapsed.TotalSeconds;
        if (remaining > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(remaining));
        }

        Console.WriteLine(
            $"Board {boardIndex}: round {round + 1}/{rounds} took {roundStopwatch.Elapsed.TotalSeconds:F1}s");
    }

    foreach (var connection in connections)
    {
        await connection.DisposeAsync();
    }
});

await Task.WhenAll(boardTasks);
overallStopwatch.Stop();

monitorCts.Cancel();
var samples = await monitorTask;

Console.WriteLine();
Console.WriteLine($"Total wall time: {overallStopwatch.Elapsed.TotalSeconds:F1}s");
Console.WriteLine($"Resource samples collected: {samples.Count}");

if (samples.Count > 0)
{
    Console.WriteLine($"CPU:    avg {samples.Average(s => s.CpuPercent):F1}%   peak {samples.Max(s => s.CpuPercent):F1}%");
    Console.WriteLine($"Memory: avg {samples.Average(s => s.MemoryMb):F0} MB   peak {samples.Max(s => s.MemoryMb):F0} MB");
}

WriteResourceChartSvg(outputSvgPath, samples);
Console.WriteLine($"Chart written to {outputSvgPath}");

/// <summary>
/// Samples the target process's CPU% (own CPU time consumed since the last tick, normalized by
/// wall-clock time and core count) and working-set memory at a fixed interval until cancelled.
/// Stops early (without failing the run) if the process can't be found or exits mid-sample.
/// </summary>
static async Task<List<ResourceSample>> MonitorResourceUsageAsync(int pid, TimeSpan interval, CancellationToken token)
{
    var samples = new List<ResourceSample>();

    Process process;
    try
    {
        process = Process.GetProcessById(pid);
    }
    catch (ArgumentException)
    {
        Console.WriteLine($"No running process with id {pid} -- skipping resource monitoring.");
        return samples;
    }

    var stopwatch = Stopwatch.StartNew();
    var lastCpuTime = process.TotalProcessorTime;
    var lastElapsed = stopwatch.Elapsed;

    while (true)
    {
        try
        {
            await Task.Delay(interval, token);
        }
        catch (OperationCanceledException)
        {
            break;
        }

        try
        {
            process.Refresh();
            var cpuTime = process.TotalProcessorTime;
            var elapsed = stopwatch.Elapsed;

            var cpuDeltaMs = (cpuTime - lastCpuTime).TotalMilliseconds;
            var wallDeltaMs = (elapsed - lastElapsed).TotalMilliseconds;
            var cpuPercent = wallDeltaMs > 0 ? cpuDeltaMs / (wallDeltaMs * Environment.ProcessorCount) * 100 : 0;
            var memoryMb = process.WorkingSet64 / 1024.0 / 1024.0;

            samples.Add(new ResourceSample(elapsed.TotalSeconds, cpuPercent, memoryMb));

            lastCpuTime = cpuTime;
            lastElapsed = elapsed;
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine("API process exited -- stopping resource monitoring early.");
            break;
        }
    }

    return samples;
}

/// <summary>
/// Renders CPU% and memory (MB) over time as two stacked line panels in a single self-contained
/// SVG -- no charting package, so nothing native to install on a headless CI runner.
/// </summary>
static void WriteResourceChartSvg(string outputPath, IReadOnlyList<ResourceSample> samples)
{
    var directory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    if (samples.Count == 0)
    {
        File.WriteAllText(outputPath,
            """<svg xmlns="http://www.w3.org/2000/svg" width="500" height="80" font-family="sans-serif"><text x="10" y="45" font-size="14">No resource samples were collected.</text></svg>""");
        return;
    }

    const int width = 1000;
    const int panelHeight = 260;
    const int panelGap = 60;
    const int marginLeft = 60;
    const int marginRight = 20;
    const int marginTop = 50;
    const int marginBottom = 40;
    const int height = marginTop + panelHeight + panelGap + panelHeight + marginBottom;
    const int plotWidth = width - marginLeft - marginRight;

    var maxTime = Math.Max(1, samples[^1].ElapsedSeconds);
    var maxCpu = Math.Max(100, samples.Max(s => s.CpuPercent) * 1.1);
    var maxMem = Math.Max(1, samples.Max(s => s.MemoryMb) * 1.15);

    var cpuPanelTop = marginTop;
    var memPanelTop = marginTop + panelHeight + panelGap;

    var cpuPoints = BuildPolylinePoints(samples, s => s.CpuPercent, maxCpu, maxTime, cpuPanelTop, panelHeight, marginLeft, plotWidth);
    var memPoints = BuildPolylinePoints(samples, s => s.MemoryMb, maxMem, maxTime, memPanelTop, panelHeight, marginLeft, plotWidth);

    var avgCpu = samples.Average(s => s.CpuPercent);
    var peakCpu = samples.Max(s => s.CpuPercent);
    var avgMem = samples.Average(s => s.MemoryMb);
    var peakMem = samples.Max(s => s.MemoryMb);

    var svg = new StringBuilder();
    svg.Append(CultureInfo.InvariantCulture, $"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" font-family="'Segoe UI', Helvetica, Arial, sans-serif">""");
    svg.Append("""<rect width="100%" height="100%" fill="white" />""");
    svg.Append(CultureInfo.InvariantCulture, $"""<text x="{width / 2}" y="24" font-size="16" font-weight="700" text-anchor="middle" fill="#0f172a">RetroBoard.Api under 10 boards x 10 participants simulated load</text>""");

    svg.Append(CultureInfo.InvariantCulture, $"""<text x="{marginLeft}" y="{cpuPanelTop - 12}" font-size="13" font-weight="600" fill="#1b7a3d">CPU -- avg {avgCpu:F1}%, peak {peakCpu:F1}%</text>""");
    AppendGridLines(svg, marginLeft, width - marginRight, cpuPanelTop, panelHeight, maxCpu, "%");
    svg.Append(CultureInfo.InvariantCulture, $"""<polyline points="{cpuPoints}" fill="none" stroke="#1b7a3d" stroke-width="2" />""");

    svg.Append(CultureInfo.InvariantCulture, $"""<text x="{marginLeft}" y="{memPanelTop - 12}" font-size="13" font-weight="600" fill="#1d4ed8">Memory -- avg {avgMem:F0} MB, peak {peakMem:F0} MB</text>""");
    AppendGridLines(svg, marginLeft, width - marginRight, memPanelTop, panelHeight, maxMem, "MB");
    svg.Append(CultureInfo.InvariantCulture, $"""<polyline points="{memPoints}" fill="none" stroke="#1d4ed8" stroke-width="2" />""");

    svg.Append(CultureInfo.InvariantCulture, $"""<text x="{width / 2}" y="{height - 10}" font-size="11" text-anchor="middle" fill="#64748b">Elapsed time: 0 - {maxTime:F0}s</text>""");
    svg.Append("</svg>");

    File.WriteAllText(outputPath, svg.ToString());
}

static string BuildPolylinePoints(
    IReadOnlyList<ResourceSample> samples, Func<ResourceSample, double> valueSelector, double maxValue,
    double maxTime, int panelTop, int panelHeight, int marginLeft, int plotWidth)
{
    var sb = new StringBuilder();
    foreach (var sample in samples)
    {
        var x = marginLeft + sample.ElapsedSeconds / maxTime * plotWidth;
        var y = panelTop + panelHeight - valueSelector(sample) / maxValue * panelHeight;
        sb.Append(x.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
          .Append(y.ToString("F1", CultureInfo.InvariantCulture)).Append(' ');
    }

    return sb.ToString();
}

static void AppendGridLines(StringBuilder svg, int left, int right, int panelTop, int panelHeight, double maxValue, string unit)
{
    for (var i = 0; i <= 4; i++)
    {
        var y = panelTop + panelHeight - panelHeight * i / 4.0;
        var value = maxValue * i / 4.0;
        svg.Append(CultureInfo.InvariantCulture, $"""<line x1="{left}" y1="{y:F1}" x2="{right}" y2="{y:F1}" stroke="#e2e8f0" stroke-width="1" />""");
        svg.Append(CultureInfo.InvariantCulture, $"""<text x="{left - 8}" y="{y + 4:F1}" font-size="11" text-anchor="end" fill="#475569">{value:F0}{unit}</text>""");
    }
}

file sealed record ResourceSample(double ElapsedSeconds, double CpuPercent, double MemoryMb);
