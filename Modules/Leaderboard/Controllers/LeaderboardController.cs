using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Modules.Leaderboard.Models;
using HauntedVoiceUniverse.Modules.Leaderboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HauntedVoiceUniverse.Modules.Leaderboard.Controllers;

[ApiController]
[Route("api/leaderboard")]
[Produces("application/json")]
public class LeaderboardController : ControllerBase
{
    private readonly ILeaderboardService _leaderboardService;

    public LeaderboardController(ILeaderboardService leaderboardService)
    {
        _leaderboardService = leaderboardService;
    }

    private Guid? CurrentUserId => User.Identity?.IsAuthenticated == true
        ? Guid.TryParse(User.FindFirstValue("uid"), out var id) ? id : null
        : null;

    // GET /api/leaderboard/{type}
    // type: daily_trending, weekly_rising, monthly_top, all_time, most_coins, most_comments, most_read_series
    /// <summary>Leaderboard dekho by type</summary>
    [HttpGet("{type}")]
    public async Task<IActionResult> GetLeaderboard(
        string type,
        [FromQuery] string? period_type = null)
    {
        var result = await _leaderboardService.GetLeaderboardAsync(type, period_type, CurrentUserId);
        return Ok(ApiResponse<List<LeaderboardEntryResponse>>.Ok(result));
    }
}

[ApiController]
[Route("api/competitions")]
[Produces("application/json")]
public class CompetitionsController : ControllerBase
{
    private readonly ILeaderboardService _leaderboardService;

    public CompetitionsController(ILeaderboardService leaderboardService)
    {
        _leaderboardService = leaderboardService;
    }

    private Guid? CurrentUserId => User.Identity?.IsAuthenticated == true
        ? Guid.TryParse(User.FindFirstValue("uid"), out var id) ? id : null
        : null;

    private Guid RequiredUserId => CurrentUserId
        ?? throw new UnauthorizedAccessException("Login required");

    // GET /api/competitions
    /// <summary>Competitions list</summary>
    [HttpGet]
    public async Task<IActionResult> GetCompetitions(
        [FromQuery] bool active_only = true,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
    {
        var result = await _leaderboardService.GetCompetitionsAsync(active_only, CurrentUserId, page, page_size);
        return Ok(ApiResponse<List<CompetitionResponse>>.Ok(result));
    }

    // GET /api/competitions/{id}
    /// <summary>Competition detail dekho</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetCompetition(Guid id)
    {
        var comp = await _leaderboardService.GetCompetitionAsync(id, CurrentUserId);
        if (comp == null) return NotFound(ApiResponse<object>.NotFound("Competition nahi mila"));
        return Ok(ApiResponse<CompetitionResponse>.Ok(comp));
    }

    // GET /api/competitions/{id}/entries
    /// <summary>Competition entries dekho</summary>
    [HttpGet("{id:guid}/entries")]
    public async Task<IActionResult> GetEntries(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
    {
        var result = await _leaderboardService.GetCompetitionEntriesAsync(id, CurrentUserId, page, page_size);
        return Ok(ApiResponse<PagedResult<CompetitionEntryResponse>>.Ok(result));
    }

    // POST /api/competitions/{id}/enter
    /// <summary>Competition mein story submit karo</summary>
    [HttpPost("{id:guid}/enter")]
    [Authorize]
    public async Task<IActionResult> SubmitEntry(Guid id, [FromBody] SubmitEntryRequest req)
    {
        var (success, message) = await _leaderboardService.SubmitEntryAsync(RequiredUserId, id, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // POST /api/competitions/{id}/entries/{entryId}/vote
    /// <summary>Entry ko vote do</summary>
    [HttpPost("{id:guid}/entries/{entryId:guid}/vote")]
    [Authorize]
    public async Task<IActionResult> Vote(Guid id, Guid entryId)
    {
        var (success, message) = await _leaderboardService.VoteForEntryAsync(RequiredUserId, id, entryId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }
}
