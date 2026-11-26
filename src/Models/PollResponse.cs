/// <summary>One person's answer to a poll.</summary>
public sealed class PollResponse
{
    public string Id { get; set; } = "";
    public string PollId { get; set; } = "";

    /// <summary>The id of the chosen <see cref="Choice"/>, not its text.</summary>
    public string ChoiceId { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>A version marker for this response. It changes on every write; edits are rejected if it no longer matches.</summary>
    public string? ETag { get; set; }
}
