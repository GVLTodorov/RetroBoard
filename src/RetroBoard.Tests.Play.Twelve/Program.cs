// Browser-driven demo recorder: the same flow as RetroBoard.Tests.Play, scaled up to 12 simulated
// participants (1 facilitator + 11 guests) to show the board holding a full-size retro instead of a
// small one. Reuses RetroBoardPlayer from RetroBoard.Tests.Play rather than duplicating it. Only the
// facilitator's session is recorded to video. Every wait targets a specific element (never
// NetworkIdle -- SignalR's persistent connection means that would never settle), so a stuck flow
// surfaces as a Playwright TimeoutException and a non-zero exit code: this doubles as a live smoke
// test of a larger board's join/write/reveal/vote/advance, not just a video-generation script.
//
// Unlike PlanningPoker.Tests.Play.Twelve, this needs no external API key -- RetroBoard has no
// optional Giphy avatar integration to skip (REQUIREMENTS.MD Section 4.1: zero required
// configuration).
//
//   dotnet run --project RetroBoard.Tests.Play.Twelve -c Release -- http://localhost:6233 artifacts/demo-video-twelve

using Microsoft.Playwright;
using RetroBoard.Tests.Play;

var baseUrl = args.Length > 0 ? args[0] : "http://localhost:6233";
var outputDir = args.Length > 1 ? args[1] : "artifacts/demo-video-twelve";

Directory.CreateDirectory(outputDir);

string[] participantNames =
    ["Jordan", "Emma", "Diego", "Aisha", "Liam", "Sofia", "Kenji", "Priya", "Mateo", "Freya", "Tariq", "Chloe"];
var viewport = new ViewportSize { Width = 1280, Height = 720 };

Console.WriteLine($"Recording demo against {baseUrl}, output dir {outputDir}");

// Blazor WASM's first page load (downloading + booting the framework) can comfortably outrun
// Playwright's default 30s action timeout on a cold, CPU-constrained CI runner, even though the
// app already answered /healthz. One throwaway request here warms Kestrel + the OS file cache
// before any timed Playwright wait starts, so it isn't the facilitator's own recorded page that
// eats it.
using (var warmupClient = new HttpClient())
{
    try
    {
        await warmupClient.GetAsync(baseUrl);
    }
    catch
    {
        // best-effort -- if this fails, the real navigation below will surface the real problem
    }
}

const int DefaultTimeoutMs = 60_000;

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

// Only the facilitator's context records video -- the recording is "how the retro looks to the
// facilitator", and keeping the other 11 sessions unrecorded keeps the artifact small and focused.
var facilitatorContext = await browser.NewContextAsync(new BrowserNewContextOptions
{
    ViewportSize = viewport,
    RecordVideoDir = outputDir,
    RecordVideoSize = new RecordVideoSize { Width = viewport.Width, Height = viewport.Height },
});
facilitatorContext.SetDefaultTimeout(DefaultTimeoutMs);

var guestContexts = new List<IBrowserContext>();
for (var i = 0; i < participantNames.Length - 1; i++)
{
    var guestContext = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = viewport });
    guestContext.SetDefaultTimeout(DefaultTimeoutMs);
    guestContexts.Add(guestContext);
}

var facilitatorPageRaw = await facilitatorContext.NewPageAsync();
LogDiagnostics(facilitatorPageRaw, "facilitator");
var facilitator = new RetroBoardPlayer(facilitatorPageRaw);

var guests = new List<RetroBoardPlayer>();
foreach (var context in guestContexts)
{
    var guestPageRaw = await context.NewPageAsync();
    LogDiagnostics(guestPageRaw, $"guest{guests.Count + 1}");
    guests.Add(new RetroBoardPlayer(guestPageRaw));
}

// Surfaces browser-side failures (a JS error, a failed WASM/framework download, etc.) directly in
// the CI log -- without this, a broken page just looks like a plain Playwright wait timeout with
// no clue why.
static void LogDiagnostics(IPage page, string label)
{
    page.Console += (_, msg) => Console.WriteLine($"[{label} console/{msg.Type}] {msg.Text}");
    page.PageError += (_, error) => Console.WriteLine($"[{label} page error] {error}");
    page.RequestFailed += (_, request) => Console.WriteLine($"[{label} request failed] {request.Url}: {request.Failure}");
    page.Response += (_, response) =>
    {
        if (response.Status >= 400)
        {
            Console.WriteLine($"[{label} response {response.Status}] {response.Url}");
        }
    };
}

var players = new List<RetroBoardPlayer> { facilitator };
players.AddRange(guests);

Console.WriteLine($"{participantNames[0]} creates the board (becomes facilitator)...");
var boardId = await facilitator.CreateBoardAndJoinAsFacilitatorAsync(baseUrl, participantNames[0], blurUntilReveal: true);
Console.WriteLine($"Board created: {boardId}");

// Guests join one at a time with a visible stagger, so the facilitator's recording shows each
// participant card appearing individually rather than all eleven popping in at once. Tighter than
// RetroBoard.Tests.Play's 500ms -- with twice as many guests, the full-delay version would make an
// already-longer recording drag even more before anything else happens.
for (var i = 0; i < guests.Count; i++)
{
    var name = participantNames[i + 1];
    Console.WriteLine($"{name} joins...");
    await guests[i].JoinBoardAsync(baseUrl, boardId, name);
    await Task.Delay(350);
}

// Default template is "Went well" (0) / "Didn't go well" (1) / "Action items" (2) -- see
// TemplateCatalog.GetColumnTitles. Every player writes one card, round-robin across columns, so
// the board fills up visibly one card at a time rather than all at once.
Console.WriteLine("Writing phase: each participant adds a card (staggered)...");
for (var i = 0; i < players.Count; i++)
{
    await players[i].AddCardAsync(i % 3, $"{participantNames[i]}'s retro thought");
    await Task.Delay(rngDelay());
}

Console.WriteLine("Facilitator reveals each blurred column...");
await facilitator.RevealAllColumnsAsync();
await Task.Delay(1_500);

Console.WriteLine("Facilitator starts voting...");
await facilitator.StartVotingAsync();
await Task.Delay(1_000);

Console.WriteLine("Voting phase: each participant casts a couple of votes (staggered)...");
for (var i = 0; i < players.Count; i++)
{
    await players[i].AddVoteAsync(i % 3, cardIndex: 0);
    await Task.Delay(200);
    await players[i].AddVoteAsync((i + 1) % 3, cardIndex: 0);
    await Task.Delay(rngDelay());
}

Console.WriteLine("Facilitator ends voting...");
await facilitator.EndVotingAsync();
await Task.Delay(3_000); // lingers so the recording shows the revealed vote counts

Console.WriteLine("Facilitator advances to action items...");
await facilitator.AdvanceToActionItemsAsync();
await Task.Delay(1_000);

Console.WriteLine("Facilitator converts the top card into an action item...");
await facilitator.ConvertToActionItemAsync(columnIndex: 0, cardIndex: 0, participantNames[1]);
await Task.Delay(3_000); // lingers on the resulting action-item list

foreach (var context in guestContexts)
{
    await context.CloseAsync();
}

var facilitatorPage = facilitator.Page;
await facilitatorContext.CloseAsync(); // flushes the video file
var videoPath = await facilitatorPage.Video!.PathAsync();

await browser.CloseAsync();

Console.WriteLine($"VIDEO_PATH={videoPath}");

static int rngDelay() => Random.Shared.Next(250, 600);
