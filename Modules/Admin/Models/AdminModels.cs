using System.ComponentModel.DataAnnotations;

namespace HauntedVoiceUniverse.Modules.Admin.Models;

// ─── WAR ROOM ────────────────────────────────────────────────────────────────

public class WarRoomResponse
{
    public long ActiveUsersToday { get; set; }
    public long NewUsersToday { get; set; }
    public long RevenueCoinsToday { get; set; }
    public long CoinsPurchasedToday { get; set; }
    public long CoinsWithdrawnToday { get; set; }
    public long PendingReports { get; set; }
    public long PendingWithdrawals { get; set; }
    public long TotalUsers { get; set; }
    public long TotalCreators { get; set; }
    public long TotalStories { get; set; }
    public long RewardFundBalance { get; set; }
    public long UnresolvedFraudAlerts { get; set; }
    public long OpenTickets { get; set; }
}

// ─── USERS ───────────────────────────────────────────────────────────────────

public class AdminUserResponse
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string Role { get; set; } = "";
    public string Status { get; set; } = "";
    public bool IsCreator { get; set; }
    public bool IsVerifiedCreator { get; set; }
    public bool IsMonetizationEnabled { get; set; }
    public int StrikeCount { get; set; }
    public long TotalStoriesPublished { get; set; }
    public long TotalFollowers { get; set; }
    public long TotalCoinsEarned { get; set; }
    public string? State { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? BannedUntil { get; set; }
    public string? BanReason { get; set; }
    public long WalletBalance { get; set; }
    public bool WalletFrozen { get; set; }
}

public class BanUserRequest
{
    [Required]
    public string Reason { get; set; } = "";
    public int? DurationHours { get; set; } // null = permanent
    public bool ShadowBan { get; set; } = false;
}

public class CreditCoinsRequest
{
    [Required]
    [Range(1, 1000000)]
    public long Amount { get; set; }

    [Required]
    public string Reason { get; set; } = "";
    public string TransactionType { get; set; } = "admin_credit";
}

public class AddStrikeRequest
{
    [Required]
    public string Reason { get; set; } = "";
    public Guid? ReportId { get; set; }
}

// ─── STORIES ─────────────────────────────────────────────────────────────────

public class AdminStoryResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Status { get; set; } = "";
    public string? ThumbnailUrl { get; set; }
    public Guid CreatorId { get; set; }
    public string CreatorUsername { get; set; } = "";
    public long TotalViews { get; set; }
    public long TotalLikes { get; set; }
    public long TotalComments { get; set; }
    public long TotalCoinsEarned { get; set; }
    public bool IsFeatured { get; set; }
    public bool IsEditorPick { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? AgeRating { get; set; }
}

public class FeatureStoryRequest
{
    public bool IsEditorPick { get; set; } = false;
    public int? HomepageBoostDays { get; set; }
}

public class RemoveStoryRequest
{
    [Required]
    public string Reason { get; set; } = "";
}

// ─── REPORTS ─────────────────────────────────────────────────────────────────

public class AdminReportResponse
{
    public Guid Id { get; set; }
    public Guid ReporterId { get; set; }
    public string ReporterUsername { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public string Reason { get; set; } = "";
    public string? CustomReason { get; set; }
    public string Severity { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByUsername { get; set; }
    public string? ActionTaken { get; set; }
    public string? ResolutionNote { get; set; }
}

public class ResolveReportRequest
{
    [Required]
    public string Action { get; set; } = ""; // 'resolved_action', 'resolved_no_action', 'dismissed'
    public string? ModerationAction { get; set; } // 'warning', 'strike', 'content_removed', 'temp_ban', 'permanent_ban', 'shadow_ban'
    public string? ResolutionNote { get; set; }
    public int? BanDurationHours { get; set; }
}

// ─── WITHDRAWALS ─────────────────────────────────────────────────────────────

public class AdminWithdrawalResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public long CoinAmount { get; set; }
    public decimal InrAmount { get; set; }
    public decimal TdsAmount { get; set; }
    public decimal NetAmount { get; set; }
    public string Status { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public string? UpiId { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankIfsc { get; set; }
    public string? AccountHolderName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? ProcessedByUsername { get; set; }
    public string? TransactionReference { get; set; }
    public string? RejectionReason { get; set; }
}

public class ApproveWithdrawalRequest
{
    [Required]
    public string TransactionReference { get; set; } = "";
}

public class RejectWithdrawalRequest
{
    [Required]
    public string Reason { get; set; } = "";
}

// ─── ANNOUNCEMENTS ───────────────────────────────────────────────────────────

public class CreateAnnouncementRequest
{
    [Required]
    [StringLength(255)]
    public string Title { get; set; } = "";

    [Required]
    public string Message { get; set; } = "";

    public string? BannerUrl { get; set; }
    public string DisplayType { get; set; } = "banner"; // banner, popup, inbox, push

    [Required]
    public DateTime StartsAt { get; set; }

    [Required]
    public DateTime EndsAt { get; set; }

    public int Priority { get; set; } = 0;
}

// ─── FRAUD ALERTS ────────────────────────────────────────────────────────────

public class FraudAlertResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Username { get; set; } = "";
    public string AlertType { get; set; } = "";
    public string Severity { get; set; } = "";
    public bool IsResolved { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ResolutionAction { get; set; }
    public string? ResolutionNote { get; set; }
}

public class ResolveFraudAlertRequest
{
    [Required]
    public string Action { get; set; } = "";
    public string? Note { get; set; }
}

// ─── ALGORITHM CONFIG ────────────────────────────────────────────────────────

public class AlgorithmConfigResponse
{
    public Guid Id { get; set; }
    public string AlgorithmName { get; set; } = "";
    public string Weights { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpdateAlgorithmConfigRequest
{
    [Required]
    public string Weights { get; set; } = ""; // JSON string
}

// ─── EMERGENCY OVERRIDE ──────────────────────────────────────────────────────

public class EmergencyOverrideResponse
{
    public string OverrideType { get; set; } = "";
    public bool IsActive { get; set; }
    public string? Reason { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public string? ActivatedBy { get; set; }
}

public class ToggleOverrideRequest
{
    public bool Activate { get; set; }
    public string? Reason { get; set; }
}

// ─── ANALYTICS ───────────────────────────────────────────────────────────────

public class PlatformAnalyticsResponse
{
    public long TotalUsers { get; set; }
    public long TotalCreators { get; set; }
    public long TotalStories { get; set; }
    public long TotalEpisodes { get; set; }
    public long TotalViews { get; set; }
    public long TotalComments { get; set; }
    public long TotalCoinsCirculation { get; set; }
    public long DauToday { get; set; }
    public long MauThisMonth { get; set; }
    public decimal RetentionRate7Day { get; set; }
    public List<DailyMetric> Last30DaysDau { get; set; } = new();
    public List<DailyMetric> Last30DaysRevenue { get; set; } = new();
}

public class DailyMetric
{
    public DateTime Date { get; set; }
    public long Value { get; set; }
}

// ─── CREATOR MANAGEMENT ──────────────────────────────────────────────────────

public class ApproveCreatorRequest
{
    public bool EnableMonetization { get; set; } = false;
}

// ─── PLATFORM SETTINGS ────────────────────────────────────────────────────────

public class PlatformSettingResponse
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Description { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpdatePlatformSettingRequest
{
    [Required]
    public string Value { get; set; } = "";
}
