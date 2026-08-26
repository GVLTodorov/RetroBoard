using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using RetroBoard.Contracts.Messages;
using RetroBoard.Contracts.Requests;

namespace RetroBoard.Contracts.Serialization;

/// <summary>
/// Source-generated (de)serializers for every request/response model crossing the wire — used for
/// both the SignalR JSON Hub Protocol and the REST endpoints' JSON options, avoiding
/// reflection-based serialization on the hottest path in the app (every card/vote change fans out to
/// every connected participant).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(BoardSummaryResponse))]
[JsonSerializable(typeof(TemplateResponse))]
[JsonSerializable(typeof(IReadOnlyList<TemplateResponse>))]
[JsonSerializable(typeof(BoardStateResponse))]
[JsonSerializable(typeof(CreateBoardRequest))]
[JsonSerializable(typeof(BoardNameSuggestionResponse))]
[JsonSerializable(typeof(JoinBoardResponse))]
[JsonSerializable(typeof(Guid?))]
public partial class RetroBoardJsonContext : JsonSerializerContext
{
    /// <summary>
    /// Builds options with this context first in the resolver chain (fast path for every model
    /// above) and a plain reflection resolver appended after it as a fallback — a
    /// <see cref="JsonSerializerContext"/> alone throws <see cref="NotSupportedException"/> for any
    /// type it wasn't told about, so the fallback is required, not optional, for e.g. SignalR's own
    /// framing types or hub method parameters we didn't explicitly list above.
    /// </summary>
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.TypeInfoResolverChain.Insert(0, Default);
        options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
        return options;
    }
}
