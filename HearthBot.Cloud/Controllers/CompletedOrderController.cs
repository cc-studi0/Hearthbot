using HearthBot.Cloud.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HearthBot.Cloud.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CompletedOrderController : ControllerBase
{
    private readonly CompletedOrderService _completedOrders;

    public CompletedOrderController(CompletedOrderService completedOrders)
    {
        _completedOrders = completedOrders;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var rows = await _completedOrders.GetVisibleAsync(DateTime.UtcNow);
        return Ok(rows);
    }

    [HttpPost("{id}/hide")]
    public async Task<IActionResult> Hide(int id)
    {
        var row = await _completedOrders.HideAsync(id, DateTime.UtcNow);
        return row == null ? NotFound() : Ok(row);
    }
}
