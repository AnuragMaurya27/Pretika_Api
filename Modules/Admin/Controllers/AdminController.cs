using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Modules.Admin.Models;
using HauntedVoiceUniverse.Modules.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

// Admin role constants for Authorize attributes:
// "super_admin", "moderator", "finance_manager", "support_agent", "content_reviewer"

namespace HauntedVoiceUniverse.Modules.Admin.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "super_admin,moderator,finance_manager,support_agent,content_reviewer")]
[Produces("application/json")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;

    public AdminController(IAdminService adminService)
    {
        _adminService = adminService;
    }

    private Guid AdminId => Guid.Parse(User.FindFirstValue("uid")!);

    // ─── WAR ROOM ───────────────────────────────────────────────────────────────

    // GET /api/admin/war-room
    /// <summary>Admin War Room - Real-time dashboard</summary>
    [HttpGet("war-room")]
    public async Task<IActionResult> GetWarRoom()
    {
        var result = await _adminService.GetWarRoomAsync();
        return Ok(ApiResponse<WarRoomResponse>.Ok(result));
    }

    // ─── USERS ──────────────────────────────────────────────────────────────────

    // GET /api/admin/users
    /// <summary>Users list with filters</summary>
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? search,
        [FromQuery] string? role,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 30)
    {
        var result = await _adminService.GetUsersAsync(search, role, status, page, page_size);
        return Ok(ApiResponse<PagedResult<AdminUserResponse>>.Ok(result));
    }

    // GET /api/admin/users/{id}
    /// <summary>User detail</summary>
    [HttpGet("users/{id:guid}")]
    public async Task<IActionResult> GetUser(Guid id)
    {
        var user = await _adminService.GetUserDetailAsync(id);
        if (user == null) return NotFound(ApiResponse<object>.NotFound("User nahi mila"));
        return Ok(ApiResponse<AdminUserResponse>.Ok(user));
    }

    // POST /api/admin/users/{id}/ban
    /// <summary>User ban karo</summary>
    [HttpPost("users/{id:guid}/ban")]
    [Authorize(Roles = "super_admin,moderator")]
    public async Task<IActionResult> BanUser(Guid id, [FromBody] BanUserRequest req)
    {
        var (success, message) = await _adminService.BanUserAsync(AdminId, id, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // POST /api/admin/users/{id}/unban
    /// <summary>User unban karo</summary>
    [HttpPost("users/{id:guid}/unban")]
    [Authorize(Roles = "super_admin,moderator")]
    public async Task<IActionResult> UnbanUser(Guid id)
    {
        var (success, message) = await _adminService.UnbanUserAsync(AdminId, id);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // POST /api/admin/users/{id}/freeze-wallet
    /// <summary>Wallet freeze karo</summary>
    [HttpPost("users/{id:guid}/freeze-wallet")]
    [Authorize(Roles = "super_admin,finance_manager")]
    public async Task<IActionResult> FreezeWallet(Guid id, [FromBody] string reason)
    {
        var (success, message) = await _adminService.FreezeWalletAsync(AdminId, id, reason);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // POST /api/admin/users/{id}/unfreeze-wallet
    /// <summary>Wallet unfreeze karo</summary>
    [HttpPost("users/{id:guid}/unfreeze-wallet")]
    [Authorize(Roles = "super_admin,finance_manager")]
    public async Task<IActionResult> UnfreezeWallet(Guid id)
    {
        var (success, message) = await _adminService.UnfreezeWalletAsync(AdminId, id);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // POST /api/admin/users/{id}/credit-coins
    /// <summary>User ko coins credit karo</summary>
    [HttpPost("users/{id:guid}/credit-coins")]
    [Authorize(Roles = "super_admin,finance_manager")]
    public async Task<IActionResult> CreditCoins(Guid id, [FromBody] CreditCoinsRequest req)
    {
        var (success, message) = await _adminService.CreditCoinsAsync(AdminId, id, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // POST /api/admin/users/{id}/strike
    /// <summary>Strike add karo</summary>
    [HttpPost("users/{id:guid}/strike")]
    [Authorize(Roles = "super_admin,moderator")]
    public async Task<IActionResult> AddStrike(Guid id, [FromBody] AddStrikeRequest req)
    {
        var (success, message) = await _adminService.AddStrikeAsync(AdminId, id, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // DELETE /api/admin/strikes/{strikeId}
    /// <summary>Strike remove karo</summary>
    [HttpDelete("strikes/{strikeId:guid}")]
    [Authorize(Roles = "super_admin,moderator")]
    public async Task<IActionResult> RemoveStrike(Guid strikeId, [FromBody] string reason)
    {
        var (success, message) = await _adminService.RemoveStrikeAsync(AdminId, strikeId, reason);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // ─── CREATOR MANAGEMENT ─────────────────────────────────────────────────────

    // POST /api/admin/users/{id}/approve-creator
    /// <summary>Creator approve karo</summary>
    [HttpPost("users/{id:guid}/approve-creator")]
    [Authorize(Roles = "super_admin,moderator")]
    public async Task<IActionResult> ApproveCreator(Guid id, [FromBody] ApproveCreatorRequest req)
    {
        var (success, message) = await _adminService.ApproveCreatorAsync(AdminId, id, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // POST /api/admin/users/{id}/toggle-monetization
    /// <summary>Monetization toggle karo</summary>
    [HttpPost("users/{id:guid}/toggle-monetization")]
    [Authorize(Roles = "super_admin,finance_manager")]
    public async Task<IActionResult> ToggleMonetization(Guid id, [FromQuery] bool enable)
    {
        var (success, message) = await _adminService.ToggleMonetizationAsync(AdminId, id, enable);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // POST /api/admin/users/{id}/verify-creator
    /// <summary>Creator verify badge do</summary>
    [HttpPost("users/{id:guid}/verify-creator")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> VerifyCreator(Guid id)
    {
        var (success, message) = await _adminService.VerifyCreatorAsync(AdminId, id);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // POST /api/admin/users/{userId}/boost-story/{storyId}
    /// <summary>Creator ki story boost karo homepage pe</summary>
    [HttpPost("users/{userId:guid}/boost-story/{storyId:guid}")]
    [Authorize(Roles = "super_admin,moderator")]
    public async Task<IActionResult> BoostCreator(Guid userId, Guid storyId)
    {
        var (success, message) = await _adminService.BoostCreatorAsync(AdminId, userId, storyId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // ─── STORIES ────────────────────────────────────────────────────────────────

    // GET /api/admin/stories
    /// <summary>Stories list with filters</summary>
    [HttpGet("stories")]
    public async Task<IActionResult> GetStories(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 30)
    {
        var result = await _adminService.GetStoriesAsync(search, status, page, page_size);
        return Ok(ApiResponse<PagedResult<AdminStoryResponse>>.Ok(result));
    }

    // POST /api/admin/stories/{id}/feature
    /// <summary>Story ko feature karo</summary>
    [HttpPost("stories/{id:guid}/feature")]
    [Authorize(Roles = "super_admin,moderator,content_reviewer")]
    public async Task<IActionResult> FeatureStory(Guid id, [FromBody] FeatureStoryRequest req)
    {
        var (success, message) = await _adminService.FeatureStoryAsync(AdminId, id, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // POST /api/admin/stories/{id}/remove
    /// <summary>Story remove karo</summary>
    [HttpPost("stories/{id:guid}/remove")]
    [Authorize(Roles = "super_admin,moderator,content_reviewer")]
    public async Task<IActionResult> RemoveStory(Guid id, [FromBody] RemoveStoryRequest req)
    {
        var (success, message) = await _adminService.RemoveStoryAsync(AdminId, id, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // ─── REPORTS ────────────────────────────────────────────────────────────────

    // GET /api/admin/reports
    /// <summary>Reports list</summary>
    [HttpGet("reports")]
    public async Task<IActionResult> GetReports(
        [FromQuery] string? status,
        [FromQuery] string? severity,
        [FromQuery] string? entity_type,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 30)
    {
        var result = await _adminService.GetReportsAsync(status, severity, entity_type, page, page_size);
        return Ok(ApiResponse<PagedResult<AdminReportResponse>>.Ok(result));
    }

    // POST /api/admin/reports/{id}/resolve
    /// <summary>Report resolve karo</summary>
    [HttpPost("reports/{id:guid}/resolve")]
    [Authorize(Roles = "super_admin,moderator,content_reviewer")]
    public async Task<IActionResult> ResolveReport(Guid id, [FromBody] ResolveReportRequest req)
    {
        var (success, message) = await _adminService.ResolveReportAsync(AdminId, id, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // ─── WITHDRAWALS ────────────────────────────────────────────────────────────

    // GET /api/admin/withdrawals
    /// <summary>Withdrawals list</summary>
    [HttpGet("withdrawals")]
    [Authorize(Roles = "super_admin,finance_manager")]
    public async Task<IActionResult> GetWithdrawals(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 30)
    {
        var result = await _adminService.GetWithdrawalsAsync(status, page, page_size);
        return Ok(ApiResponse<PagedResult<AdminWithdrawalResponse>>.Ok(result));
    }

    // POST /api/admin/withdrawals/{id}/approve
    /// <summary>Withdrawal approve karo</summary>
    [HttpPost("withdrawals/{id:guid}/approve")]
    [Authorize(Roles = "super_admin,finance_manager")]
    public async Task<IActionResult> ApproveWithdrawal(Guid id, [FromBody] ApproveWithdrawalRequest req)
    {
        var (success, message) = await _adminService.ApproveWithdrawalAsync(AdminId, id, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // POST /api/admin/withdrawals/{id}/reject
    /// <summary>Withdrawal reject karo</summary>
    [HttpPost("withdrawals/{id:guid}/reject")]
    [Authorize(Roles = "super_admin,finance_manager")]
    public async Task<IActionResult> RejectWithdrawal(Guid id, [FromBody] RejectWithdrawalRequest req)
    {
        var (success, message) = await _adminService.RejectWithdrawalAsync(AdminId, id, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // ─── ANNOUNCEMENTS ──────────────────────────────────────────────────────────

    // POST /api/admin/announcements
    /// <summary>Announcement create karo</summary>
    [HttpPost("announcements")]
    [Authorize(Roles = "super_admin,moderator")]
    public async Task<IActionResult> CreateAnnouncement([FromBody] CreateAnnouncementRequest req)
    {
        var (success, message) = await _adminService.CreateAnnouncementAsync(AdminId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // DELETE /api/admin/announcements/{id}
    /// <summary>Announcement delete karo</summary>
    [HttpDelete("announcements/{id:guid}")]
    [Authorize(Roles = "super_admin,moderator")]
    public async Task<IActionResult> DeleteAnnouncement(Guid id)
    {
        var (success, message) = await _adminService.DeleteAnnouncementAsync(AdminId, id);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // ─── FRAUD ALERTS ───────────────────────────────────────────────────────────

    // GET /api/admin/fraud-alerts
    /// <summary>Fraud alerts dekho</summary>
    [HttpGet("fraud-alerts")]
    [Authorize(Roles = "super_admin,finance_manager")]
    public async Task<IActionResult> GetFraudAlerts(
        [FromQuery] bool unresolved_only = true,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 30)
    {
        var result = await _adminService.GetFraudAlertsAsync(unresolved_only, page, page_size);
        return Ok(ApiResponse<PagedResult<FraudAlertResponse>>.Ok(result));
    }

    // POST /api/admin/fraud-alerts/{id}/resolve
    /// <summary>Fraud alert resolve karo</summary>
    [HttpPost("fraud-alerts/{id:guid}/resolve")]
    [Authorize(Roles = "super_admin,finance_manager")]
    public async Task<IActionResult> ResolveFraudAlert(Guid id, [FromBody] ResolveFraudAlertRequest req)
    {
        var (success, message) = await _adminService.ResolveFraudAlertAsync(AdminId, id, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // ─── ALGORITHM CONFIG ───────────────────────────────────────────────────────

    // GET /api/admin/algorithm-config
    /// <summary>Algorithm configs dekho</summary>
    [HttpGet("algorithm-config")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> GetAlgorithmConfigs()
    {
        var result = await _adminService.GetAlgorithmConfigsAsync();
        return Ok(ApiResponse<List<AlgorithmConfigResponse>>.Ok(result));
    }

    // PUT /api/admin/algorithm-config/{name}
    /// <summary>Algorithm config update karo</summary>
    [HttpPut("algorithm-config/{name}")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> UpdateAlgorithmConfig(string name, [FromBody] UpdateAlgorithmConfigRequest req)
    {
        var (success, message) = await _adminService.UpdateAlgorithmConfigAsync(AdminId, name, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // ─── EMERGENCY OVERRIDES ────────────────────────────────────────────────────

    // GET /api/admin/emergency-overrides
    /// <summary>Emergency override states dekho</summary>
    [HttpGet("emergency-overrides")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> GetEmergencyOverrides()
    {
        var result = await _adminService.GetEmergencyOverridesAsync();
        return Ok(ApiResponse<List<EmergencyOverrideResponse>>.Ok(result));
    }

    // POST /api/admin/emergency-overrides/{type}/toggle
    // type: disable_chat, disable_transactions, freeze_withdrawals, lock_platform
    /// <summary>Emergency override toggle karo</summary>
    [HttpPost("emergency-overrides/{type}/toggle")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> ToggleOverride(string type, [FromBody] ToggleOverrideRequest req)
    {
        var (success, message) = await _adminService.ToggleOverrideAsync(AdminId, type, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // ─── ANALYTICS ──────────────────────────────────────────────────────────────

    // GET /api/admin/analytics
    /// <summary>Platform analytics</summary>
    [HttpGet("analytics")]
    public async Task<IActionResult> GetAnalytics()
    {
        var result = await _adminService.GetAnalyticsAsync();
        return Ok(ApiResponse<PlatformAnalyticsResponse>.Ok(result));
    }
}
