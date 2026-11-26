/// <summary>One option in a poll. The id stays the same when the text is edited, so stored responses keep pointing at it.</summary>
public sealed class Choice
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>A poll with its choices.</summary>
public sealed class Poll
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<Choice> Choices { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>A version marker for this poll. It changes on every write; edits are rejected if it no longer matches.</summary>
    public string? ETag { get; set; }
}
