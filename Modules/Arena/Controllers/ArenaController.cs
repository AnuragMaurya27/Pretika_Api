using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Modules.Arena.Models;
using HauntedVoiceUniverse.Modules.Arena.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HauntedVoiceUniverse.Modules.Arena.Controllers;

// ═══════════════════════════════════════════════════════════════════════════
//  DARR ARENA — Controller  (/api/arena/...)
// ═══════════════════════════════════════════════════════════════════════════

[ApiController]
[Route("api/arena")]
[Produces("application/json")]
public class ArenaController : ControllerBase
{
    private readonly IArenaService _arena;

    public ArenaController(IArenaService arena)
    {
        _arena = arena;
    }

    private Guid? CurrentUserId => User.Identity?.IsAuthenticated == true
        ? Guid.TryParse(User.FindFirstValue("uid"), out var id) ? id : null
        : null;

    private Guid RequiredUserId => CurrentUserId
        ?? throw new UnauthorizedAccessException("Login required");

    // ───────────────────────────────────────────────────────────────────────
    //  PUBLIC / BROWSING
    // ───────────────────────────────────────────────────────────────────────

    // GET /api/arena/events
    [HttpGet("events")]
    [AllowAnonymous]
    public async Task<IActionResult> GetEvents(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
    {
        var result = await _arena.GetEventsAsync(CurrentUserId, status, page, page_size);
        return Ok(ApiResponse<PagedResult<ArenaEventResponse>>.Ok(result));
    }

    // GET /api/arena/events/{eventId}
    [HttpGet("events/{eventId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetEvent(Guid eventId)
    {
        var ev = await _arena.GetEventByIdAsync(eventId, CurrentUserId);
        if (ev == null) return NotFound(ApiResponse<object>.NotFound("Event nahi mila"));
        return Ok(ApiResponse<ArenaEventResponse>.Ok(ev));
    }

    // GET /api/arena/events/{eventId}/stats  (admin/mod only)
    [HttpGet("events/{eventId:guid}/stats")]
    [Authorize(Roles = "super_admin,moderator")]
    public async Task<IActionResult> GetEventStats(Guid eventId)
    {
        var stats = await _arena.GetEventStatsAsync(eventId);
        if (stats == null) return NotFound(ApiResponse<object>.NotFound("Event nahi mila"));
        return Ok(ApiResponse<ArenaEventStatsResponse>.Ok(stats));
    }

    // GET /api/arena/hall-of-champions
    [HttpGet("hall-of-champions")]
    [AllowAnonymous]
    public async Task<IActionResult> GetHallOfChampions(
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 10)
    {
        var hall = await _arena.GetHallOfChampionsAsync(page, page_size);
        return Ok(ApiResponse<List<HallOfChampionsResponse>>.Ok(hall));
    }

    // GET /api/arena/events/{eventId}/most-feared
    [HttpGet("events/{eventId:guid}/most-feared")]
    [AllowAnonymous]
    public async Task<IActionResult> GetMostFearedStory(Guid eventId)
    {
        var story = await _arena.GetMostFearedStoryAsync(eventId);
        if (story == null)
            return NotFound(ApiResponse<object>.NotFound("Event complete nahi hua ya koi winner nahi"));
        return Ok(ApiResponse<MostFearedStoryResponse>.Ok(story));
    }

    // GET /api/arena/events/{eventId}/results
    [HttpGet("events/{eventId:guid}/results")]
    [AllowAnonymous]
    public async Task<IActionResult> GetEventResults(Guid eventId)
    {
        var results = await _arena.GetEventResultsAsync(eventId);
        if (results == null)
            return NotFound(ApiResponse<object>.NotFound("Results abhi available nahi hain"));
        return Ok(ApiResponse<ArenaResultsResponse>.Ok(results));
    }

    // ───────────────────────────────────────────────────────────────────────
    //  ADMIN — EVENT MANAGEMENT
    // ───────────────────────────────────────────────────────────────────────

    // POST /api/arena/admin/events
    [HttpPost("admin/events")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> CreateEvent([FromBody] CreateEventRequest req)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<object>.Fail(
                ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).First()));

        var (success, message, data) = await _arena.CreateEventAsync(RequiredUserId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<ArenaEventResponse>.Ok(data!, message));
    }

    // PUT /api/arena/admin/events/{eventId}
    [HttpPut("admin/events/{eventId:guid}")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> UpdateEvent(Guid eventId, [FromBody] UpdateEventRequest req)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<object>.Fail(
                ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).First()));

        var (success, message) = await _arena.UpdateEventAsync(RequiredUserId, eventId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse.OkNoData(message));
    }

    // POST /api/arena/admin/events/{eventId}/cancel
    [HttpPost("admin/events/{eventId:guid}/cancel")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> CancelEvent(Guid eventId, [FromBody] CancelEventRequest req)
    {
        var (success, message) = await _arena.CancelEventAsync(RequiredUserId, eventId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse.OkNoData(message));
    }

    // ───────────────────────────────────────────────────────────────────────
    //  PARTICIPANT FLOW
    // ───────────────────────────────────────────────────────────────────────

    // POST /api/arena/events/{eventId}/join
    [HttpPost("events/{eventId:guid}/join")]
    [Authorize]
    public async Task<IActionResult> JoinEvent(Guid eventId)
    {
        var (success, message) = await _arena.JoinEventAsync(RequiredUserId, eventId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse.OkNoData(message));
    }

    // GET /api/arena/events/{eventId}/my-story
    [HttpGet("events/{eventId:guid}/my-story")]
    [Authorize]
    public async Task<IActionResult> GetMyStory(Guid eventId)
    {
        var story = await _arena.GetMyStoryAsync(RequiredUserId, eventId);
        if (story == null)
            return NotFound(ApiResponse<object>.NotFound("Story nahi mili — pehle event join karo"));
        return Ok(ApiResponse<ArenaStoryResponse>.Ok(story));
    }

    // GET /api/arena/events/{eventId}/submit-status
    [HttpGet("events/{eventId:guid}/submit-status")]
    [Authorize]
    public async Task<IActionResult> GetSubmitStatus(Guid eventId)
    {
        var status = await _arena.GetSubmitStatusAsync(RequiredUserId, eventId);
        if (status == null) return NotFound(ApiResponse<object>.NotFound("Event nahi mila"));
        return Ok(ApiResponse<SubmitStatusResponse>.Ok(status));
    }

    // PUT /api/arena/events/{eventId}/draft  (auto-save every 30s)
    [HttpPut("events/{eventId:guid}/draft")]
    [Authorize]
    public async Task<IActionResult> SaveDraft(Guid eventId, [FromBody] SaveDraftRequest req)
    {
        var (success, message) = await _arena.SaveDraftAsync(RequiredUserId, eventId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse.OkNoData(message));
    }

    // POST /api/arena/events/{eventId}/submit
    [HttpPost("events/{eventId:guid}/submit")]
    [Authorize]
    public async Task<IActionResult> SubmitStory(Guid eventId)
    {
        var (success, message) = await _arena.SubmitStoryAsync(RequiredUserId, eventId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse.OkNoData(message));
    }

    // POST /api/arena/events/{eventId}/questions
    [HttpPost("events/{eventId:guid}/questions")]
    [Authorize]
    public async Task<IActionResult> SubmitQuestions(Guid eventId, [FromBody] SubmitQuestionsRequest req)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<object>.Fail(
                ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).First()));

        var (success, message) = await _arena.SubmitQuestionsAsync(RequiredUserId, eventId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse.OkNoData(message));
    }

    // ───────────────────────────────────────────────────────────────────────
    //  REVIEW PHASE
    // ───────────────────────────────────────────────────────────────────────

    // GET /api/arena/events/{eventId}/assignments
    [HttpGet("events/{eventId:guid}/assignments")]
    [Authorize]
    public async Task<IActionResult> GetMyAssignments(Guid eventId)
    {
        var assignments = await _arena.GetMyAssignmentsAsync(RequiredUserId, eventId);
        return Ok(ApiResponse<List<ArenaAssignmentResponse>>.Ok(assignments));
    }

    // GET /api/arena/assignments/{assignmentId}/story  (blind — no author info)
    [HttpGet("assignments/{assignmentId:guid}/story")]
    [Authorize]
    public async Task<IActionResult> GetStoryForReview(Guid assignmentId)
    {
        var story = await _arena.GetStoryForReviewAsync(RequiredUserId, assignmentId);
        if (story == null)
            return NotFound(ApiResponse<object>.NotFound("Assignment nahi mila ya access nahi"));
        return Ok(ApiResponse<ArenaStoryReviewResponse>.Ok(story));
    }

    // POST /api/arena/assignments/{assignmentId}/verify-read
    [HttpPost("assignments/{assignmentId:guid}/verify-read")]
    [Authorize]
    public async Task<IActionResult> VerifyReadTime(Guid assignmentId)
    {
        var (success, message) = await _arena.VerifyReadTimeAsync(RequiredUserId, assignmentId);
        // Always 200 — client polls; success=false means "not yet, keep waiting"
        return Ok(new { success, message });
    }

    // POST /api/arena/assignments/{assignmentId}/verify-scroll
    [HttpPost("assignments/{assignmentId:guid}/verify-scroll")]
    [Authorize]
    public async Task<IActionResult> VerifyScroll(Guid assignmentId)
    {
        var (success, message) = await _arena.VerifyScrollAsync(RequiredUserId, assignmentId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse.OkNoData(message));
    }

    // POST /api/arena/assignments/{assignmentId}/answer-questions
    [HttpPost("assignments/{assignmentId:guid}/answer-questions")]
    [Authorize]
    public async Task<IActionResult> AnswerQuestions(
        Guid assignmentId, [FromBody] AnswerQuestionsRequest req)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<object>.Fail(
                ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).First()));

        var (success, message, data) = await _arena.AnswerQuestionsAsync(RequiredUserId, assignmentId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        // Return 200 even if disqualified — Flutter needs the payload to show the disqualification screen
        return Ok(ApiResponse<AnswerQuestionsResult>.Ok(data!, message));
    }

    // POST /api/arena/assignments/{assignmentId}/rating
    [HttpPost("assignments/{assignmentId:guid}/rating")]
    [Authorize]
    public async Task<IActionResult> SubmitRating(
        Guid assignmentId, [FromBody] SubmitRatingRequest req)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<object>.Fail(
                ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).First()));

        var (success, message) = await _arena.SubmitRatingAsync(RequiredUserId, assignmentId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse.OkNoData(message));
    }

    // ───────────────────────────────────────────────────────────────────────
    //  MY RESULTS
    // ───────────────────────────────────────────────────────────────────────

    // GET /api/arena/events/{eventId}/my-result
    [HttpGet("events/{eventId:guid}/my-result")]
    [Authorize]
    public async Task<IActionResult> GetMyResult(Guid eventId)
    {
        var result = await _arena.GetMyResultAsync(RequiredUserId, eventId);
        if (result == null) return NotFound(ApiResponse<object>.NotFound("Result nahi mila"));
        return Ok(ApiResponse<MyArenaResultResponse>.Ok(result));
    }

    // ───────────────────────────────────────────────────────────────────────
    //  PROFILE BADGES
    // ───────────────────────────────────────────────────────────────────────

    // GET /api/arena/users/{userId}/badges
    [HttpGet("users/{userId:guid}/badges")]
    [AllowAnonymous]
    public async Task<IActionResult> GetUserBadges(Guid userId)
    {
        var badges = await _arena.GetUserBadgesAsync(userId);
        return Ok(ApiResponse<List<ArenaBadgeResponse>>.Ok(badges));
    }

    // GET /api/arena/me/badges
    [HttpGet("me/badges")]
    [Authorize]
    public async Task<IActionResult> GetMyBadges()
    {
        var badges = await _arena.GetUserBadgesAsync(RequiredUserId);
        return Ok(ApiResponse<List<ArenaBadgeResponse>>.Ok(badges));
    }
}
