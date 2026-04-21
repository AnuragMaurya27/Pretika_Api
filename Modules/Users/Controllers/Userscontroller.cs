using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Modules.Users.Models;
using HauntedVoiceUniverse.Modules.Users.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HauntedVoiceUniverse.Modules.Users.Controllers;

[ApiController]
[Route("api/users")]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    private Guid? CurrentUserId => User.Identity?.IsAuthenticated == true
        ? Guid.TryParse(User.FindFirstValue("uid"), out var id) ? id : null
        : null;

    private Guid RequiredUserId => CurrentUserId
        ?? throw new UnauthorizedAccessException("Login required");

    // ─── GET MY PROFILE ───────────────────────────────────────────────────────
    /// <summary>Apna poora profile dekho (private data bhi)</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMyProfile()
    {
        var profile = await _userService.GetMyProfileAsync(RequiredUserId);
        if (profile == null)
            return NotFound(ApiResponse<object>.NotFound("Profile nahi mila"));

        return Ok(ApiResponse<MyProfileResponse>.Ok(profile));
    }

    // ─── SEARCH USERS ─────────────────────────────────────────────────────────
    /// <summary>Username se user search karo</summary>
    [HttpGet("search")]
    public async Task<IActionResult> SearchUsers(
        [FromQuery] string q = "",
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 10)
    {
        var result = await _userService.SearchUsersAsync(q, page, Math.Min(page_size, 50));
        return Ok(ApiResponse<List<FollowUserResponse>>.Ok(result));
    }

    // ─── GET PUBLIC PROFILE ───────────────────────────────────────────────────
    /// <summary>Kisi bhi user ka public profile dekho</summary>
    [HttpGet("{username}")]
    public async Task<IActionResult> GetPublicProfile(string username)
    {
        var profile = await _userService.GetPublicProfileAsync(username, CurrentUserId);
        if (profile == null)
            return NotFound(ApiResponse<object>.NotFound("User nahi mila"));

        return Ok(ApiResponse<UserProfileResponse>.Ok(profile));
    }

    // ─── UPDATE PROFILE ───────────────────────────────────────────────────────
    /// <summary>Apna profile update karo</summary>
    [HttpPut("me")]
    [Authorize]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest req)
    {
        var (success, message) = await _userService.UpdateProfileAsync(RequiredUserId, req);
        if (!success)
            return BadRequest(ApiResponse<object>.Fail(message));

        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // ─── UPDATE AVATAR ────────────────────────────────────────────────────────
    /// <summary>Avatar update karo (JPG/PNG/WebP, max 5MB)</summary>
    [HttpPut("me/avatar")]
    [Authorize]
    public async Task<IActionResult> UpdateAvatar(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("File upload karo"));

        var (success, message, url) = await _userService.UpdateAvatarAsync(RequiredUserId, file);
        if (!success)
            return BadRequest(ApiResponse<object>.Fail(message));

        return Ok(ApiResponse<object>.Ok(new { avatar_url = url }, message));
    }

    // ─── UPDATE COVER IMAGE ───────────────────────────────────────────────────
    /// <summary>Cover image update karo (JPG/PNG/WebP, max 5MB)</summary>
    [HttpPut("me/cover")]
    [Authorize]
    public async Task<IActionResult> UpdateCoverImage(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("File upload karo"));

        var (success, message, url) = await _userService.UpdateCoverImageAsync(RequiredUserId, file);
        if (!success)
            return BadRequest(ApiResponse<object>.Fail(message));

        return Ok(ApiResponse<object>.Ok(new { cover_image_url = url }, message));
    }

    // ─── DELETE ACCOUNT ───────────────────────────────────────────────────────
    /// <summary>Account delete karo — DPDP Act 2023 compliant PII erasure</summary>
    [HttpDelete("me")]
    [Authorize]
    public async Task<IActionResult> DeleteAccount()
    {
        var (success, message) = await _userService.DeleteAccountAsync(RequiredUserId);
        if (!success)
            return BadRequest(ApiResponse<object>.Fail(message));

        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // VULN#20 FIX (DPDP Act 2023 — Right to Access, Section 11):
    // Users have the right to receive a copy of their personal data.
    // This endpoint returns all PII held about the requesting user in a portable format.
    /// <summary>Apna personal data download karo (DPDP Act 2023 — Right to Access)</summary>
    [HttpGet("me/data-export")]
    [Authorize]
    public async Task<IActionResult> ExportMyData()
    {
        var profile = await _userService.GetMyProfileAsync(RequiredUserId);
        if (profile == null) return NotFound(ApiResponse<object>.Fail("User nahi mila"));

        // Return a structured export. In production this should be queued as a background
        // job and emailed to the user's verified address to prevent account enumeration.
        var export = new
        {
            exported_at = DateTime.UtcNow,
            dpdp_notice = "DPDP Act 2023 Section 11 — Right to Access Personal Data",
            personal_data = new
            {
                profile.Id,
                profile.Username,
                profile.Email,
                profile.DisplayName,
                profile.Bio,
                profile.Phone,
                profile.Gender,
                profile.DateOfBirth,
                profile.State,
                profile.City,
                profile.Pincode,
                profile.PreferredLanguage,
                profile.CreatedAt,
                profile.LastActiveAt,
            },
            activity_summary = new
            {
                profile.TotalFollowers,
                profile.TotalFollowing,
                profile.TotalStoriesPublished,
                profile.TotalViewsReceived,
                profile.LoginStreak,
                profile.MaxLoginStreak,
                profile.TotalReadingTimeMinutes,
                profile.TotalReferrals,
            }
        };

        return Ok(ApiResponse<object>.Ok(export, "Aapka personal data. Yeh DPDP Act 2023 ke Section 11 ke antargat provide kiya gaya hai."));
    }

    // ─── FOLLOW ───────────────────────────────────────────────────────────────
    /// <summary>Kisi user ko follow karo</summary>
    [HttpPost("{targetId:guid}/follow")]
    [Authorize]
    public async Task<IActionResult> FollowUser(Guid targetId)
    {
        var (success, message) = await _userService.FollowUserAsync(RequiredUserId, targetId);
        if (!success)
            return BadRequest(ApiResponse<object>.Fail(message));

        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // ─── UNFOLLOW ─────────────────────────────────────────────────────────────
    /// <summary>Kisi user ko unfollow karo</summary>
    [HttpDelete("{targetId:guid}/follow")]
    [Authorize]
    public async Task<IActionResult> UnfollowUser(Guid targetId)
    {
        var (success, message) = await _userService.UnfollowUserAsync(RequiredUserId, targetId);
        if (!success)
            return BadRequest(ApiResponse<object>.Fail(message));

        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // ─── GET FOLLOWERS ────────────────────────────────────────────────────────
    /// <summary>Kisi user ke followers dekho</summary>
    [HttpGet("{userId:guid}/followers")]
    public async Task<IActionResult> GetFollowers(
        Guid userId,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
    {
        var pagination = new PaginationParams { Page = page, PageSize = Math.Min(page_size, 100) };
        var result = await _userService.GetFollowersAsync(userId, CurrentUserId, pagination);
        return Ok(ApiResponse<PagedResult<FollowUserResponse>>.Ok(result));
    }

    // ─── GET FOLLOWING ────────────────────────────────────────────────────────
    /// <summary>Kisi user ki following list dekho</summary>
    [HttpGet("{userId:guid}/following")]
    public async Task<IActionResult> GetFollowing(
        Guid userId,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
    {
        var pagination = new PaginationParams { Page = page, PageSize = Math.Min(page_size, 100) };
        var result = await _userService.GetFollowingAsync(userId, CurrentUserId, pagination);
        return Ok(ApiResponse<PagedResult<FollowUserResponse>>.Ok(result));
    }

    // ─── BLOCK ────────────────────────────────────────────────────────────────
    /// <summary>Kisi user ko block karo</summary>
    [HttpPost("{targetId:guid}/block")]
    [Authorize]
    public async Task<IActionResult> BlockUser(Guid targetId)
    {
        var (success, message) = await _userService.BlockUserAsync(RequiredUserId, targetId);
        if (!success)
            return BadRequest(ApiResponse<object>.Fail(message));

        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // ─── UNBLOCK ──────────────────────────────────────────────────────────────
    /// <summary>Kisi user ko unblock karo</summary>
    [HttpDelete("{targetId:guid}/block")]
    [Authorize]
    public async Task<IActionResult> UnblockUser(Guid targetId)
    {
        var (success, message) = await _userService.UnblockUserAsync(RequiredUserId, targetId);
        if (!success)
            return BadRequest(ApiResponse<object>.Fail(message));

        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // ─── BECOME CREATOR ───────────────────────────────────────────────────────
    /// <summary>Creator ban jao (onboarding flow)</summary>
    [HttpPost("me/become-creator")]
    [Authorize]
    public async Task<IActionResult> BecomeCreator()
    {
        var (success, message, coinsAwarded) = await _userService.BecomeCreatorAsync(RequiredUserId);
        if (!success)
            return BadRequest(ApiResponse<object>.Fail(message));

        return Ok(ApiResponse<object>.Ok(new { coins_awarded = coinsAwarded }, message));
    }

    // ─── COMPLETE ONBOARDING ──────────────────────────────────────────────────
    /// <summary>Onboarding complete mark karo (reader path)</summary>
    [HttpPost("me/complete-onboarding")]
    [Authorize]
    public async Task<IActionResult> CompleteOnboarding()
    {
        var (success, message) = await _userService.CompleteOnboardingAsync(RequiredUserId);
        if (!success)
            return BadRequest(ApiResponse<object>.Fail(message));

        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // ─── GET REFERRAL STATS ───────────────────────────────────────────────────
    /// <summary>Apni referral stats aur referred users list dekho</summary>
    [HttpGet("me/referrals")]
    [Authorize]
    public async Task<IActionResult> GetMyReferrals()
    {
        var stats = await _userService.GetReferralStatsAsync(RequiredUserId);
        return Ok(ApiResponse<ReferralStatsResponse>.Ok(stats));
    }

    // ─── GET BLOCKED USERS ────────────────────────────────────────────────────
    /// <summary>Apni blocked users list dekho</summary>
    [HttpGet("me/blocked")]
    [Authorize]
    public async Task<IActionResult> GetBlockedUsers(
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
    {
        var pagination = new PaginationParams { Page = page, PageSize = Math.Min(page_size, 100) };
        var result = await _userService.GetBlockedUsersAsync(RequiredUserId, pagination);
        return Ok(ApiResponse<PagedResult<FollowUserResponse>>.Ok(result));
    }

    // ─── REGISTER FCM DEVICE TOKEN ────────────────────────────────────────────
    /// <summary>FCM device token register karo (login ke baad call karo)</summary>
    [HttpPost("me/device")]
    [Authorize]
    public async Task<IActionResult> RegisterDevice([FromBody] RegisterDeviceRequest req)
    {
        var (success, message) = await _userService.RegisterDeviceAsync(
            RequiredUserId,
            req.DeviceToken,
            req.DeviceType,
            req.AppVersion ?? "1.0.0");

        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }
}