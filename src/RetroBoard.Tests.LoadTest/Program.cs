// Small SignalR load-driver: spins up N boards x M participants, has every participant write K
// cards (round-robin across the board's columns) concurrently, and reports p50/p95/p99/max latency
// for the AddCard round trip -- the hottest path in the app, since every card add fans out an
// individualized per-viewer state snapshot to every connected participant on that board (see
// BoardHub.BroadcastBoardStateAsync). Once every board has finished writing, each board's
// facilitator also exports it (REQUIREMENTS.MD Section 5.7), timed and verified the same way.
// Mirrors PlanningPoker.Tests.LoadTest's shape; not gated in CI (REQUIREMENTS.MD Section 9's
// load/soak-test bullet) -- run manually against a live instance:
//
//   dotnet run --project RetroBoard.Tests.LoadTest -c Release -- http://localhost:6233 100 100 5

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using RetroBoard.Contracts;
using RetroBoard.Contracts.Messages;
using RetroBoard.Contracts.Requests;
using RetroBoard.Contracts.Serialization;

var baseUrl = args.Length > 0 ? args[0] : "http://localhost:6233";
var boardCount = args.Length > 1 ? int.Parse(args[1]) : 20;
var participantsPerBoard = args.Length > 2 ? int.Parse(args[2]) : 5;
var cardsPerParticipant = args.Length > 3 ? int.Parse(args[3]) : 5;

Console.WriteLine(
    $"Load test: {boardCount} boards x {participantsPerBoard} participants x {cardsPerParticipant} cards each, then one export per board, against {baseUrl}");

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

ReportLatencies("AddCard round-trip", addCardLatenciesMs);
Console.WriteLine();
ReportLatencies("Export round-trip", exportLatenciesMs);

Console.WriteLine();
Console.WriteLine($"Total wall time: {overallStopwatch.Elapsed.TotalSeconds:F2}s");
Console.WriteLine($"Boards: {boardCount}, exports attempted: {boardCount}, export failures: {exportFailures.Count}");

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
