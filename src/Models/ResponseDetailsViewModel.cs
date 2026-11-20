/// <summary>Everything the response page needs: the poll it belongs to and the response being edited.</summary>
public sealed class ResponseDetailsViewModel
{
    public required Poll Poll { get; init; }
    public required PollResponse Response { get; init; }
    public string? Error { get; init; }
}
