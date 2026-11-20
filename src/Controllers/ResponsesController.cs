using Microsoft.AspNetCore.Mvc;

/// <summary>The page for changing one response. Responses belong to a poll, so the poll id is part of the address.</summary>
public sealed class ResponsesController(PollStore polls, ResponseStore responses) : Controller
{
    private const string ConflictMessage =
        "Someone else changed this response first, so your change was not saved. The latest version is shown below.";

    [HttpGet("/polls/{pollId}/responses/{id}")]
    public async Task<IActionResult> Details(string pollId, string id, [FromQuery] string? error)
    {
        var poll = await polls.GetAsync(pollId);
        var response = await responses.GetAsync(pollId, id);
        if (poll is null || response is null) return NotFound();

        return View(new ResponseDetailsViewModel { Poll = poll, Response = response, Error = error });
    }

    [HttpPost("/polls/{pollId}/responses/{id}/edit")]
    public async Task<IActionResult> Edit(
        string pollId, string id, [FromForm] string? eTag, [FromForm] string? choiceId)
    {
        var poll = await polls.GetAsync(pollId);
        if (poll is null) return NotFound();

        if (choiceId is null || poll.Choices.All(choice => choice.Id != choiceId))
        {
            return RedirectToResponse(pollId, id, "Pick one of the choices.");
        }

        var outcome = await responses.UpdateChoiceAsync(pollId, id, eTag ?? "", choiceId);
        return outcome switch
        {
            UpdateOutcome.Updated => Redirect($"/polls/{pollId}"),
            UpdateOutcome.Conflict => RedirectToResponse(pollId, id, ConflictMessage),
            _ => NotFound(),
        };
    }

    private IActionResult RedirectToResponse(string pollId, string id, string error) =>
        Redirect($"/polls/{pollId}/responses/{id}?error=" + Uri.EscapeDataString(error));
}
