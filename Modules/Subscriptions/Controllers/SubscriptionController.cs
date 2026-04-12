using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Modules.Subscriptions.Models;
using HauntedVoiceUniverse.Modules.Subscriptions.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HauntedVoiceUniverse.Modules.Subscriptions.Controllers;

[ApiController]
[Route("api/subscriptions")]
[Produces("application/json")]
public class SubscriptionController : ControllerBase
{
    private readonly ISubscriptionService _svc;

    public SubscriptionController(ISubscriptionService svc)
    {
        _svc = svc;
    }

    private Guid RequiredUserId => Guid.TryParse(User.FindFirstValue("uid"), out var id) ? id
        : throw new UnauthorizedAccessException("Login required");

    // GET /api/subscriptions/plans
    [HttpGet("plans")]
    public IActionResult GetPlans()
    {
        var plans = _svc.GetPlans();
        return Ok(ApiResponse<List<SubscriptionPlan>>.Ok(plans));
    }

    // GET /api/subscriptions/my
    [HttpGet("my")]
    [Authorize]
    public async Task<IActionResult> GetMySubscription()
    {
        var status = await _svc.GetStatusAsync(RequiredUserId);
        return Ok(ApiResponse<SubscriptionStatusResponse>.Ok(status));
    }

    // POST /api/subscriptions/purchase
    [HttpPost("purchase")]
    [Authorize]
    public async Task<IActionResult> Purchase([FromBody] PurchaseSubscriptionRequest req)
    {
        var (success, message, data) = await _svc.PurchaseAsync(RequiredUserId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<UserSubscriptionResponse>.Ok(data!, message));
    }
}
