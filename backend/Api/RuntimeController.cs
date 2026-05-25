using Microsoft.AspNetCore.Mvc;
using ProviderStudio.Runtime;

namespace ProviderStudio.Api;

[ApiController, Route("api/runtime")]
public sealed class RuntimeController : ControllerBase
{
    private readonly RuntimeManager _runtime;

    public RuntimeController(RuntimeManager runtime) => _runtime = runtime;

    [HttpGet("status")]
    public IActionResult GetAllStatuses() => Ok(_runtime.GetAllStatuses());

    [HttpGet("status/{providerId}")]
    public IActionResult GetStatus(string providerId)
    {
        var s = _runtime.GetStatus(providerId);
        if (s is null) return NotFound(new { error = "No active session for this provider." });
        return Ok(s);
    }

    [HttpPost("{providerId}/start")]
    public async Task<IActionResult> Start(string providerId, CancellationToken ct)
    {
        try
        {
            await _runtime.StartProviderAsync(providerId, ct);
            return Ok(new { message = "Started" });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (Exception ex)            { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpPost("{providerId}/stop")]
    public async Task<IActionResult> Stop(string providerId)
    {
        await _runtime.StopProviderAsync(providerId);
        return Ok(new { message = "Stopped" });
    }

    [HttpPost("{providerId}/restart")]
    public async Task<IActionResult> Restart(string providerId, CancellationToken ct)
    {
        try
        {
            await _runtime.RestartProviderAsync(providerId, ct);
            return Ok(new { message = "Restarted" });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (Exception ex)            { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpPost("start-all")]
    public IActionResult StartAll()
    {
        // Providers already started by RuntimeManager on boot.
        // This just returns current status.
        return Ok(_runtime.GetAllStatuses());
    }

    [HttpPost("stop-all")]
    public async Task<IActionResult> StopAll()
    {
        var statuses = _runtime.GetAllStatuses();
        foreach (var s in statuses)
            await _runtime.StopProviderAsync(s.ProviderId);
        return Ok(new { message = $"Stopped {statuses.Count} sessions" });
    }
}
