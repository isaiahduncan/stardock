using Newtonsoft.Json;

/// <summary>One option in a poll. The id stays the same when the text is edited, so stored responses keep pointing at it.</summary>
public sealed class Choice
{
    [JsonProperty("id")] public string Id { get; set; } = "";
    [JsonProperty("text")] public string Text { get; set; } = "";
}

/// <summary>A poll document. Stored in the polls container, partitioned by its own id.</summary>
public sealed class Poll
{
    [JsonProperty("id")] public string Id { get; set; } = "";
    [JsonProperty("title")] public string Title { get; set; } = "";
    [JsonProperty("description")] public string Description { get; set; } = "";
    [JsonProperty("choices")] public List<Choice> Choices { get; set; } = [];
    [JsonProperty("createdAt")] public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Cosmos's version tag for this document. It changes on every write; edits are rejected if it no longer matches.</summary>
    [JsonProperty("_etag", NullValueHandling = NullValueHandling.Ignore)] public string? ETag { get; set; }
}
