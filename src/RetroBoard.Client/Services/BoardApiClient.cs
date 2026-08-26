using System.Net.Http.Json;
using System.Text.Json;
using RetroBoard.Contracts;
using RetroBoard.Contracts.Requests;
using RetroBoard.Contracts.Serialization;

namespace RetroBoard.Client.Services;

/// <summary>Thin typed wrapper over the REST endpoints in <c>Api/Endpoints/BoardEndpoints.cs</c>.</summary>
public sealed class BoardApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = RetroBoardJsonContext.CreateOptions();

    private readonly HttpClient _httpClient;

    public BoardApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> GetBoardNameSuggestionAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<BoardNameSuggestionResponse>(
            "/api/boards/name-suggestion", JsonOptions, cancellationToken);
        return response?.Name ?? string.Empty;
    }

    public async Task<IReadOnlyList<TemplateResponse>> GetTemplatesAsync(CancellationToken cancellationToken = default) =>
        await _httpClient.GetFromJsonAsync<List<TemplateResponse>>("/api/templates", JsonOptions, cancellationToken) ?? [];

    public async Task<BoardSummaryResponse?> CreateBoardAsync(
        string name, TemplateType template, bool blurUntilReveal, int? voteBudget, int? maxVotesPerCard,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardRequest(name, template, blurUntilReveal, voteBudget, maxVotesPerCard),
            JsonOptions,
            cancellationToken);

        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<BoardSummaryResponse>(JsonOptions, cancellationToken)
            : null;
    }

    public async Task<BoardSummaryResponse?> GetBoardAsync(string boardId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/boards/{boardId}", cancellationToken);

        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<BoardSummaryResponse>(JsonOptions, cancellationToken)
            : null;
    }

    public async Task<string?> ExportBoardMarkdownAsync(
        string boardId, Guid participantId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/boards/{boardId}/export?participantId={participantId}", cancellationToken);

        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken) : null;
    }
}
