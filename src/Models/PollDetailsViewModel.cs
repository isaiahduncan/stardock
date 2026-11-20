/// <summary>Everything the poll page needs: the poll, its responses, and whether the page is in edit mode.</summary>
public sealed class PollDetailsViewModel
{
    public required Poll Poll { get; init; }
    public required IReadOnlyList<PollResponse> Responses { get; init; }
    public bool Editing { get; init; }
    public string? Error { get; init; }

    /// <summary>How many responses picked the given choice.</summary>
    public int CountFor(string choiceId) => Responses.Count(response => response.ChoiceId == choiceId);

    /// <summary>The text of a choice, for showing a response's answer.</summary>
    public string ChoiceText(string choiceId) =>
        Poll.Choices.FirstOrDefault(choice => choice.Id == choiceId)?.Text ?? "(unknown choice)";
}
