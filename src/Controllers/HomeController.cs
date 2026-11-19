using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>The HTML page, its form handlers, and the health check.</summary>
public sealed class HomeController(CosmosStore store) : Controller
{
    [HttpGet("/")]
    public async Task<IActionResult> Index([FromQuery] string? error)
    {
        ViewData["Error"] = error;
        return View(await store.ListAsync(null));
    }

    [HttpPost("/items")]
    public async Task<IActionResult> Create([FromForm] string? partitionKey, [FromForm] string? json)
    {
        partitionKey = partitionKey?.Trim() ?? "";
        if (partitionKey.Length == 0) return RedirectWithError("Partition key is required.");

        JObject document;
        try
        {
            document = JObject.Parse(json ?? "");
        }
        catch (JsonReaderException)
        {
            return RedirectWithError("The document must be a JSON object.");
        }

        await store.AddAsync(partitionKey, document);
        return Redirect("/"); // redirect after POST so a refresh doesn't resubmit the form
    }

    [HttpPost("/items/{partitionKey}/{id}/delete")]
    public async Task<IActionResult> Delete(string partitionKey, string id)
    {
        await store.DeleteAsync(partitionKey, id);
        return Redirect("/");
    }

    [HttpGet("/healthz")]
    public IActionResult Health() => Ok(new { ok = true }); // the app only starts once Cosmos answered

    private IActionResult RedirectWithError(string message) =>
        Redirect("/?error=" + Uri.EscapeDataString(message));
}
