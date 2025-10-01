using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace PlaywrightMcpServer;

public sealed partial class PlaywrightTools
{
    private sealed record DialogResult(string Type, string Message, string? DefaultValue);

    public sealed record FormField(
        [property: JsonPropertyName("selector")] string Selector,
        [property: JsonPropertyName("value")] string? Value,
        [property: JsonPropertyName("action")] string? Action,
        [property: JsonPropertyName("values")] string[]? Values);

    public sealed record ConsoleMessageEntry
    {
        [JsonPropertyName("timestamp")] public DateTimeOffset Timestamp { get; init; }
        [JsonPropertyName("type")] public string Type { get; init; } = string.Empty;
        [JsonPropertyName("text")] public string Text { get; init; } = string.Empty;
        [JsonPropertyName("args")][AllowNull] public string[]? Args { get; init; }
    }

    public sealed class NetworkRequestEntry
    {
        [JsonPropertyName("timestamp")] public DateTimeOffset Timestamp { get; init; }
        [JsonPropertyName("method")] public string Method { get; init; } = string.Empty;
        [JsonPropertyName("url")] public string Url { get; init; } = string.Empty;
        [JsonPropertyName("resourceType")][AllowNull] public string? ResourceType { get; init; }
        [JsonPropertyName("status")][AllowNull] public int? Status { get; set; }
        [JsonPropertyName("failure")][AllowNull] public string? Failure { get; set; }

        public NetworkRequestEntry Clone() => new()
        {
            Timestamp = Timestamp,
            Method = Method,
            Url = Url,
            ResourceType = ResourceType,
            Status = Status,
            Failure = Failure
        };
    }
}
