using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RetroBoard.Contracts;
using RetroBoard.Contracts.Requests;
using RetroBoard.Contracts.Serialization;
using RetroBoard.Tests.Integration.TestSupport;
using Xunit;

namespace RetroBoard.Tests.Integration;

/// <summary>Covers the REST surface in BoardEndpoints.cs that BoardHubTests.cs never touches -- that
/// file only ever calls the happy path of POST /api/boards before moving on to SignalR.</summary>
public class BoardEndpointsTests : IClassFixture<RetroBoardWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = RetroBoardJsonContext.CreateOptions();

    private readonly RetroBoardWebApplicationFactory _factory;

    public BoardEndpointsTests(RetroBoardWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetNameSuggestion_ReturnsAName()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/boards/name-suggestion");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<BoardNameSuggestionResponse>(JsonOptions);
        Assert.False(string.IsNullOrWhiteSpace(body!.Name));
    }

    [Fact]
    public async Task GetTemplates_ReturnsEveryTemplateType()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/templates");

        response.EnsureSuccessStatusCode();
        var templates = await response.Content.ReadFromJsonAsync<List<TemplateResponse>>(JsonOptions);
        Assert.Equal(Enum.GetValues<TemplateType>().Length, templates!.Count);
    }

    [Fact]
    public async Task GetHealthz_ReturnsOk()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetBoard_ReturnsTheBoard_WhenItExists()
    {
        using var client = _factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardRequest("Get Board Test", TemplateType.StartStopContinue, false, null, null),
            JsonOptions);
        var board = await createResponse.Content.ReadFromJsonAsync<BoardSummaryResponse>(JsonOptions);

        var response = await client.GetAsync($"/api/boards/{board!.BoardId}");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetBoard_ReturnsNotFound_WhenTheBoardDoesNotExist()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/boards/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetBoard_ReturnsNotFound_WhenTheIdHasNoUsableCharacters()
    {
        // A distinct short-circuit from the "well-formed but unknown id" case above -- this one never
        // even reaches the repository lookup because BoardId.TryParse itself rejects the input.
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/boards/!!!");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostBoard_ReturnsBadRequest_WhenNameIsBlank()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/boards", new CreateBoardRequest("   ", TemplateType.StartStopContinue, false, null, null), JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostBoard_ReturnsConflict_WhenTheNameIsAlreadyTaken()
    {
        using var client = _factory.CreateClient();
        var first = await client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardRequest("Duplicate Name Board", TemplateType.StartStopContinue, false, null, null),
            JsonOptions);
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardRequest("Duplicate Name Board", TemplateType.MadSadGlad, false, null, null),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task PostBoard_UsesDefaultVoteBudgetAndMaxVotesPerCard_WhenOmitted()
    {
        using var client = _factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardRequest("Defaults Board", TemplateType.StartStopContinue, false, null, null),
            JsonOptions);

        createResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetExport_ReturnsForbidden_ForNonFacilitator()
    {
        using var client = _factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardRequest("Export Auth Board", TemplateType.StartStopContinue, false, null, null),
            JsonOptions);
        var board = await createResponse.Content.ReadFromJsonAsync<BoardSummaryResponse>(JsonOptions);

        var response = await client.GetAsync($"/api/boards/{board!.BoardId}/export?participantId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetExport_ReturnsNotFound_ForUnknownBoard()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/boards/does-not-exist/export?participantId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
