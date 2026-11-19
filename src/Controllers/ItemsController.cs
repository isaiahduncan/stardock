using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>JSON API for documents. Bodies are parsed by hand because documents are untyped JSON.</summary>
[ApiController]
[Route("api/items")]
public sealed class ItemsController(CosmosStore store) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? partitionKey)
    {
        var items = await store.ListAsync(partitionKey);
        return Content(JsonConvert.SerializeObject(items), "application/json");
    }

    [HttpPost("{partitionKey}")]
    public async Task<IActionResult> Create(string partitionKey)
    {
        using var reader = new StreamReader(Request.Body);

        JObject document;
        try
        {
            document = JObject.Parse(await reader.ReadToEndAsync());
        }
        catch (JsonReaderException)
        {
            return BadRequest(new { error = "The body must be a JSON object." });
        }

        var saved = await store.AddAsync(partitionKey, document);
        return Content(saved.ToString(), "application/json");
    }

    [HttpDelete("{partitionKey}/{id}")]
    public async Task<IActionResult> Delete(string partitionKey, string id)
    {
        if (await store.DeleteAsync(partitionKey, id)) return NoContent();
        return NotFound();
    }
}
