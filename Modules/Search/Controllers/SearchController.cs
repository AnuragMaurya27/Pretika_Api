using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Modules.Search.Models;
using HauntedVoiceUniverse.Modules.Search.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HauntedVoiceUniverse.Modules.Search.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController : ControllerBase
{
    private readonly ISearchService _searchService;

    public SearchController(ISearchService searchService)
    {
        _searchService = searchService;
    }

    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(HvuClaims.UserId), out var id) ? id : null;

    // ─── UNIFIED SEARCH ───────────────────────────────────────────────────────
    /// <summary>
    /// Unified search: stories + users ek saath
    /// ?q=...&searchType=all|stories|users&sortBy=trending&language=hindi&category=horror&page=1
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Search([FromQuery] UnifiedSearchRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Q))
            return Ok(ApiResponse<UnifiedSearchResponse>.Ok(new UnifiedSearchResponse
            {
                Query = "",
                SearchType = req.SearchType,
                Stories = new SearchStoriesPage(),
                Users = [],
            }, "Search query empty"));

        var result = await _searchService.SearchAsync(req, CurrentUserId);
        return Ok(ApiResponse<UnifiedSearchResponse>.Ok(result));
    }

    // ─── CREATE REPORT ────────────────────────────────────────────────────────
    /// <summary>
    /// User report submit karo (story / episode / user / comment)
    /// POST /api/search/reports
    /// </summary>
    [HttpPost("reports")]
    [Authorize]
    public async Task<IActionResult> CreateReport([FromBody] CreateReportRequest req)
    {
        if (CurrentUserId == null) return Unauthorized(ApiResponse.Fail("Login required", 401));

        var (ok, msg) = await _searchService.CreateReportAsync(CurrentUserId.Value, req);
        if (!ok) return BadRequest(ApiResponse.Fail(msg));

        return Ok(ApiResponse.OkNoData(msg));
    }
}
