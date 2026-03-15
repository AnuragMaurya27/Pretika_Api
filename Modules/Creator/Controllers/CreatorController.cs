using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Modules.Creator.Models;
using HauntedVoiceUniverse.Modules.Creator.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HauntedVoiceUniverse.Modules.Creator.Controllers;

[ApiController]
[Route("api/creator")]
[Produces("application/json")]
[Authorize]
public class CreatorController : ControllerBase
{
    private readonly ICreatorService _creatorService;

    public CreatorController(ICreatorService creatorService)
    {
        _creatorService = creatorService;
    }

    private Guid RequiredUserId =>
        Guid.TryParse(User.FindFirstValue("uid"), out var id) ? id
        : throw new UnauthorizedAccessException("Login required");

    // GET /api/creator/stats
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var (success, message, data) = await _creatorService.GetStatsAsync(RequiredUserId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<CreatorStatsResponse>.Ok(data!, message));
    }

    // GET /api/creator/earnings
    [HttpGet("earnings")]
    public async Task<IActionResult> GetEarningsHub()
    {
        try
        {
            var data = await _creatorService.GetEarningsHubAsync(RequiredUserId);
            return Ok(ApiResponse<EarningsHubResponse>.Ok(data));
        }
        catch (Exception ex) { return StatusCode(500, ApiResponse<object>.Fail(ex.Message)); }
    }

    // GET /api/creator/earnings/premium-unlock
    [HttpGet("earnings/premium-unlock")]
    public async Task<IActionResult> GetPremiumUnlock()
    {
        try
        {
            var data = await _creatorService.GetPremiumUnlockEarningsAsync(RequiredUserId);
            return Ok(ApiResponse<List<PremiumUnlockStoryDetail>>.Ok(data));
        }
        catch (Exception ex) { return StatusCode(500, ApiResponse<object>.Fail(ex.Message)); }
    }

    // GET /api/creator/earnings/appreciations?page=1&pageSize=30
    [HttpGet("earnings/appreciations")]
    public async Task<IActionResult> GetAppreciations([FromQuery] int page = 1, [FromQuery] int pageSize = 30)
    {
        try
        {
            var (items, total) = await _creatorService.GetAppreciationEarningsAsync(RequiredUserId, page, pageSize);
            return Ok(ApiResponse<object>.Ok(new { items, total, page, page_size = pageSize }));
        }
        catch (Exception ex) { return StatusCode(500, ApiResponse<object>.Fail(ex.Message)); }
    }

    // GET /api/creator/earnings/super-chat?page=1&pageSize=30
    [HttpGet("earnings/super-chat")]
    public async Task<IActionResult> GetSuperChat([FromQuery] int page = 1, [FromQuery] int pageSize = 30)
    {
        try
        {
            var (items, total) = await _creatorService.GetSuperChatEarningsAsync(RequiredUserId, page, pageSize);
            return Ok(ApiResponse<object>.Ok(new { items, total, page, page_size = pageSize }));
        }
        catch (Exception ex) { return StatusCode(500, ApiResponse<object>.Fail(ex.Message)); }
    }

    // GET /api/creator/earnings/competitions
    [HttpGet("earnings/competitions")]
    public async Task<IActionResult> GetCompetitions()
    {
        try
        {
            var data = await _creatorService.GetCompetitionEarningsAsync(RequiredUserId);
            return Ok(ApiResponse<List<CompetitionEarningDetail>>.Ok(data));
        }
        catch (Exception ex) { return StatusCode(500, ApiResponse<object>.Fail(ex.Message)); }
    }

    // GET /api/creator/earnings/referrals
    [HttpGet("earnings/referrals")]
    public async Task<IActionResult> GetReferrals()
    {
        try
        {
            var data = await _creatorService.GetReferralEarningsAsync(RequiredUserId);
            return Ok(ApiResponse<List<ReferralEarningDetail>>.Ok(data));
        }
        catch (Exception ex) { return StatusCode(500, ApiResponse<object>.Fail(ex.Message)); }
    }

    // GET /api/creator/earnings/chart?period=daily&from=2024-01-01&to=2024-12-31
    [HttpGet("earnings/chart")]
    public async Task<IActionResult> GetChart(
        [FromQuery] string period = "daily",
        [FromQuery] string? from = null,
        [FromQuery] string? to = null)
    {
        try
        {
            var fromDate = DateTime.TryParse(from, out var f) ? f : DateTime.Now.AddDays(-29);
            var toDate   = DateTime.TryParse(to,   out var t) ? t : DateTime.Now;
            var data = await _creatorService.GetEarningsChartAsync(RequiredUserId, period, fromDate, toDate);
            return Ok(ApiResponse<EarningsChartResponse>.Ok(data));
        }
        catch (Exception ex) { return StatusCode(500, ApiResponse<object>.Fail(ex.Message)); }
    }
}
