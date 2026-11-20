using Newtonsoft.Json;

/// <summary>One person's answer to a poll. Stored in the responses container, partitioned by poll id.</summary>
public sealed class PollResponse
{
    [JsonProperty("id")] public string Id { get; set; } = "";
    [JsonProperty("pollId")] public string PollId { get; set; } = "";

    /// <summary>The id of the chosen <see cref="Choice"/>, not its text.</summary>
    [JsonProperty("choiceId")] public string ChoiceId { get; set; } = "";

    [JsonProperty("createdAt")] public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Cosmos's version tag for this document. It changes on every write; edits are rejected if it no longer matches.</summary>
    [JsonProperty("_etag", NullValueHandling = NullValueHandling.Ignore)] public string? ETag { get; set; }
}
