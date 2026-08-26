// SignalR load-driver: spins up N boards x M participants, has every participant write K cards
// (round-robin across the board's columns) concurrently, and reports p50/p95/p99/max latency for
// the AddCard round trip -- the hottest path in the app, since every card add fans out an
// individualized per-viewer state snapshot to every connected participant on that board (see
// BoardHub.BroadcastBoardStateAsync). Once every board has finished writing, each board's
// facilitator also exports it (REQUIREMENTS.MD Section 5.7), timed and verified the same way.
// While all of that runs, it also samples RetroBoard.Api's own CPU% and working-set memory at a
// fixed interval and renders the series as a dependency-free SVG line chart -- the same resource
// curve RetroBoard.Tests.Play.Hundred used to draw on its own, now folded into this one tool so
// there's a single load driver instead of two. Mirrors PlanningPoker.Tests.LoadTest's shape; not
// gated in CI (REQUIREMENTS.MD Section 9's load/soak-test bullet) -- run manually against a live
// instance:
//
//   dotnet run --project RetroBoard.Tests.LoadTest -c Release -- \
//     http://localhost:6233 <api-pid> docs/load-resource-usage.svg 100 5 3

using System.Collections.Concurrent;
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
var outputSvgPath = args.Length > 2 ? args[2] : "docs/load-resource-usage.svg";
var boardCount = args.Length > 3 ? int.Parse(args[3]) : 100;
var participantsPerBoard = args.Length > 4 ? int.Parse(args[4]) : 5;
var cardsPerParticipant = args.Length > 5 ? int.Parse(args[5]) : 3;

Console.WriteLine(
    $"Load test: {boardCount} boards x {participantsPerBoard} participants x {cardsPerParticipant} cards each, then one export per board, against {baseUrl}");

var monitorCts = new CancellationTokenSource();
// Unlike RetroBoard.Tests.Play.Hundred's old 15-20s-per-round pacing, this load is an unpaced
// concurrent burst -- at realistic scale (100 boards x 5 participants x 3 cards) it's over in a
// couple of seconds, so the sampling interval is much tighter than a "sustained load" tool would
// need, purely to still catch enough points across that short window for a legible chart.
var monitorTask = apiPid > 0
    ? MonitorResourceUsageAsync(apiPid, TimeSpan.FromMilliseconds(100), monitorCts.Token)
    : Task.FromResult(new List<ResourceSample>());

if (apiPid <= 0)
{
    Console.WriteLine("No API process id given -- skipping CPU/memory sampling, running the load only.");
}

var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
var jsonOptions = RetroBoardJsonContext.CreateOptions();
var addCardLatenciesMs = new ConcurrentBag<double>();
var exportLatenciesMs = new ConcurrentBag<double>();
var exportFailures = new ConcurrentBag<string>();

// Board names must be unique for the whole lifetime of the target server, not just within one run
// -- a board only disappears some time after its last participant leaves (the empty-board grace
// period), so a bare "Load {boardIndex}" would collide with the previous run's still-lingering
// boards. A run-specific prefix sidesteps that without needing to wait it out.
var runId = Guid.NewGuid().ToString("N")[..8];

// Every participant's HubConnection is a real, persistently-held-open TCP socket for the whole
// board lifecycle below -- at boardCount x participantsPerBoard connections launched all at once
// (e.g. 100 x 100 = 10,000), a single Windows client machine can run out of ephemeral local ports
// well before the server itself is under any real strain (confirmed locally: an untamed run failed
// with SocketException 10048 "Only one usage of each socket address..."). Capping how many boards
// are actively connected at once keeps peak concurrent sockets around 2,000, comfortably inside
// Windows' default dynamic port range -- this paces *arrival*, it doesn't shrink the scale being
// tested: all boardCount boards and all their participants still get driven, just in waves instead
// of one instantaneous burst (which isn't how 100 real teams would start a retro at once, either).
var maxConcurrentBoards = Math.Max(1, 2_000 / participantsPerBoard);
using var boardGate = new SemaphoreSlim(maxConcurrentBoards);

var overallStopwatch = Stopwatch.StartNew();

var boardTasks = Enumerable.Range(0, boardCount).Select(async boardIndex =>
{
    await boardGate.WaitAsync();
    try
    {
        await RunBoardAsync(boardIndex);
    }
    finally
    {
        boardGate.Release();
    }
});

await Task.WhenAll(boardTasks);
overallStopwatch.Stop();

monitorCts.Cancel();
var samples = await monitorTask;

ReportLatencies("AddCard round-trip", addCardLatenciesMs);
Console.WriteLine();
ReportLatencies("Export round-trip", exportLatenciesMs);

Console.WriteLine();
Console.WriteLine($"Total wall time: {overallStopwatch.Elapsed.TotalSeconds:F2}s");
Console.WriteLine($"Boards: {boardCount}, exports attempted: {boardCount}, export failures: {exportFailures.Count}");
Console.WriteLine($"Resource samples collected: {samples.Count}");

if (samples.Count > 0)
{
    Console.WriteLine($"CPU:    avg {samples.Average(s => s.CpuPercent):F1}%   peak {samples.Max(s => s.CpuPercent):F1}%");
    Console.WriteLine($"Memory: avg {samples.Average(s => s.MemoryMb):F0} MB   peak {samples.Max(s => s.MemoryMb):F0} MB");
}

WriteResourceChartSvg(outputSvgPath, samples, boardCount, participantsPerBoard);
Console.WriteLine($"Chart written to {outputSvgPath}");

foreach (var failure in exportFailures)
{
    Console.WriteLine($"  FAILED: {failure}");
}

if (!exportFailures.IsEmpty)
{
    Environment.Exit(1);
}

async Task RunBoardAsync(int boardIndex)
{
    var createResponse = await httpClient.PostAsJsonAsync(
        "/api/boards",
        new CreateBoardRequest($"Load {runId} {boardIndex}", TemplateType.WentWellDidntWork, BlurUntilReveal: false, VoteBudget: null, MaxVotesPerCard: null),
        jsonOptions);

    if (!createResponse.IsSuccessStatusCode)
    {
        var body = await createResponse.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"Board {boardIndex} creation failed: {(int)createResponse.StatusCode} {body}");
    }

    var board = await createResponse.Content.ReadFromJsonAsync<BoardSummaryResponse>(jsonOptions)
        ?? throw new InvalidOperationException($"Board {boardIndex} creation returned an empty body.");

    var connections = new List<HubConnection>();
    Guid facilitatorId = default;
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
            // First joiner is the facilitator server-side -- see BoardHub.JoinBoard / Board.AddParticipant.
            facilitatorId = joined.ParticipantId;
            columnIds = joined.State.Columns.Select(c => c.ColumnId).ToList();
        }
    }

    // Every participant writes their cards concurrently, spread round-robin across the board's
    // columns so no single column absorbs the whole load.
    var writeTasks = connections.Select(async (connection, participantIndex) =>
    {
        for (var c = 0; c < cardsPerParticipant; c++)
        {
            var columnId = columnIds[(participantIndex + c) % columnIds.Count];
            var stopwatch = Stopwatch.StartNew();
            await connection.InvokeAsync("AddCard", columnId, $"Card {participantIndex}-{c}");
            addCardLatenciesMs.Add(stopwatch.Elapsed.TotalMilliseconds);
        }
    });
    await Task.WhenAll(writeTasks);

    // Only the facilitator can export (enforced server-side, not just client-hidden) -- exercises
    // REQUIREMENTS.MD Section 5.7 under the same load the board was just written under.
    var exportStopwatch = Stopwatch.StartNew();
    var exportResponse = await httpClient.GetAsync($"/api/boards/{board.BoardId}/export?participantId={facilitatorId}");
    exportLatenciesMs.Add(exportStopwatch.Elapsed.TotalMilliseconds);

    if (!exportResponse.IsSuccessStatusCode)
    {
        exportFailures.Add($"Board {boardIndex}: export returned {(int)exportResponse.StatusCode}");
    }
    else
    {
        var markdown = await exportResponse.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            exportFailures.Add($"Board {boardIndex}: export returned empty markdown");
        }
    }

    foreach (var connection in connections)
    {
        await connection.DisposeAsync();
    }
}

static void ReportLatencies(string label, ConcurrentBag<double> samplesMs)
{
    var sorted = samplesMs.OrderBy(x => x).ToList();

    double Percentile(double p)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Clamp(Math.Round(p * (sorted.Count - 1)), 0, sorted.Count - 1);
        return sorted[index];
    }

    Console.WriteLine($"{label} samples: {sorted.Count}");
    Console.WriteLine($"  p50: {Percentile(0.50):F2} ms");
    Console.WriteLine($"  p95: {Percentile(0.95):F2} ms");
    Console.WriteLine($"  p99: {Percentile(0.99):F2} ms");
    Console.WriteLine($"  max: {(sorted.Count > 0 ? sorted[^1] : 0):F2} ms");
}

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
static void WriteResourceChartSvg(string outputPath, IReadOnlyList<ResourceSample> samples, int boardCount, int participantsPerBoard)
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
    svg.Append(CultureInfo.InvariantCulture, $"""<text x="{width / 2}" y="24" font-size="16" font-weight="700" text-anchor="middle" fill="#0f172a">RetroBoard.Api under {boardCount} boards x {participantsPerBoard} participants load</text>""");

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
