using Microsoft.AspNetCore.Mvc;

/// <summary>The poll pages: the list with the create form, and one poll's page with its responses and the edit form.</summary>
public sealed class PollsController(PollStore polls, ResponseStore responses) : Controller
{
    private const string ConflictMessage =
        "Someone else changed this poll first, so your change was not saved. The latest version is shown below.";

    [HttpGet("/")]
    public async Task<IActionResult> Index([FromQuery] string? error)
    {
        ViewData["Error"] = error;
        return View(await polls.ListAsync());
    }

    [HttpPost("/polls")]
    public async Task<IActionResult> Create(
        [FromForm] string? title, [FromForm] string? description, [FromForm] string? choices)
    {
        title = title?.Trim() ?? "";
        var choiceTexts = SplitLines(choices);

        if (title.Length == 0) return RedirectToIndex("A title is required.");
        if (choiceTexts.Count < 2) return RedirectToIndex("A poll needs at least two choices, one per line.");

        var poll = await polls.CreateAsync(title, description?.Trim() ?? "", choiceTexts);
        return Redirect($"/polls/{poll.Id}"); // redirect after POST so a refresh doesn't resubmit the form
    }

    [HttpGet("/polls/{id}")]
    public async Task<IActionResult> Details(string id, [FromQuery] bool edit, [FromQuery] string? error)
    {
        var poll = await polls.GetAsync(id);
        if (poll is null) return NotFound();

        var model = new PollDetailsViewModel
        {
            Poll = poll,
            Responses = await responses.ListAsync(id),
            Editing = edit,
            Error = error,
        };
        return View(model);
    }

    [HttpPost("/polls/{id}/respond")]
    public async Task<IActionResult> Respond(string id, [FromForm] string? choiceId)
    {
        var poll = await polls.GetAsync(id);
        if (poll is null) return NotFound();

        if (choiceId is null || poll.Choices.All(choice => choice.Id != choiceId))
        {
            return RedirectToPoll(id, error: "Pick one of the choices.");
        }

        await responses.AddAsync(id, choiceId);
        return RedirectToPoll(id);
    }

    [HttpPost("/polls/{id}/edit")]
    public async Task<IActionResult> Edit(
        string id,
        [FromForm] string? eTag,
        [FromForm] string? title,
        [FromForm] string? description,
        [FromForm] string[]? choiceIds,
        [FromForm] string[]? choiceTexts,
        [FromForm] string[]? newChoices)
    {
        title = title?.Trim() ?? "";
        if (title.Length == 0) return RedirectToPoll(id, edit: true, error: "A title is required.");

        // The existing choices arrive as two parallel lists: their ids (hidden fields) and their texts.
        var renamed = new Dictionary<string, string>();
        var ids = choiceIds ?? Array.Empty<string>();
        var texts = choiceTexts ?? Array.Empty<string>();
        for (var i = 0; i < ids.Length && i < texts.Length; i++)
        {
            var text = texts[i].Trim();
            if (text.Length == 0) return RedirectToPoll(id, edit: true, error: "A choice can't be blank.");
            renamed[ids[i]] = text;
        }

        var added = (newChoices ?? Array.Empty<string>()).Select(text => text.Trim()).Where(text => text.Length > 0).ToList();

        var outcome = await polls.UpdateAsync(id, eTag ?? "", title, description?.Trim() ?? "", renamed, added);
        return outcome switch
        {
            UpdateOutcome.Updated => RedirectToPoll(id),
            UpdateOutcome.Conflict => RedirectToPoll(id, error: ConflictMessage),
            _ => NotFound(),
        };
    }

    private static List<string> SplitLines(string? text) =>
        (text ?? "")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

    private IActionResult RedirectToIndex(string error) =>
        Redirect("/?error=" + Uri.EscapeDataString(error));

    private IActionResult RedirectToPoll(string id, bool edit = false, string? error = null)
    {
        var query = new List<string>();
        if (edit) query.Add("edit=true");
        if (error is not null) query.Add("error=" + Uri.EscapeDataString(error));
        return Redirect($"/polls/{id}" + (query.Count > 0 ? "?" + string.Join("&", query) : ""));
    }
}
