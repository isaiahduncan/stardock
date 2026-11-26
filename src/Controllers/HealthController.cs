using Microsoft.AspNetCore.Mvc;

public sealed class HealthController : Controller
{
    [HttpGet("/healthz")]
    public IActionResult Health() => Ok(new { ok = true }); // the app only starts once Postgres answered
}
