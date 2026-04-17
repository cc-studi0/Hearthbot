using HearthBot.Cloud.Models.Learning;
using HearthBot.Cloud.Services.Learning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HearthBot.Cloud.Controllers.Learning;

[ApiController]
[Route("v1/learning/samples")]
[Authorize(Policy = "MachineOnly")]
public class SamplesController : ControllerBase
{
    private readonly ILogger<SamplesController> _logger;

    public SamplesController(ILogger<SamplesController> logger)
    {
        _logger = logger;
    }

    [HttpPost("batch")]
    public IActionResult Batch([FromBody] SampleBatchRequest req)
    {
        var machineId = MachineTokenService.ExtractMachineId(User);
        if (string.IsNullOrEmpty(machineId))
            return Unauthorized(new { error = "missing machine_id claim" });

        var count = req?.Samples?.Count ?? 0;
        _logger.LogInformation("Received {Count} samples from {MachineId}", count, machineId);

        return Ok(new SampleBatchResponse
        {
            Accepted = count,
            Duplicates = 0
        });
    }
}
