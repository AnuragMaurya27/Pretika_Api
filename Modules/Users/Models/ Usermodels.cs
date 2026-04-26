using System.ComponentModel.DataAnnotations;

namespace HauntedVoiceUniverse.Modules.Users.Models;

// ─── Response DTOs ────────────────────────────────────────────────────────────

public class UserProfileResponse
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public string? CoverImageUrl { get; set; }
    public string Role { get; set; } = "";
    public bool IsCreator { get; set; }
    public bool IsVerifiedCreator { get; set; }
    public bool IsEmailVerified { get; set; }
    public string ReaderFearRank { get; set; } = "";
    public string? CreatorFearRank { get; set; }
    public decimal CreatorRankScore { get; set; }
    public decimal ReaderRankScore { get; set; }
    public string? State { get; set; }
    public string? City { get; set; }
    public string PreferredLanguage { get; set; } = "hindi";

    // Stats
    public int TotalFollowers { get; set; }
    public int TotalFollowing { get; set; }
    public int TotalStoriesPublished { get; set; }
    public long TotalViewsReceived { get; set; }
    public long TotalCoinsEarned { get; set; }
    public int LoginStreak { get; set; }

    // Viewer context (only when logged in)
    public bool? IsFollowing { get; set; }
    public bool? IsFollowedByThem { get; set; }
    public bool? IsBlockedByMe { get; set; }

    public string? ReferralCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastActiveAt { get; set; }
}

public class MyProfileResponse : UserProfileResponse
{
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string? Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Pincode { get; set; }
    public bool Is2FAEnabled { get; set; }
    public bool IsMonetizationEnabled { get; set; }
    public long CoinBalance { get; set; }
    public long TotalCoinsSpent { get; set; }
    public bool OnboardingCompleted { get; set; }
    public int TotalReferrals { get; set; }
    public int MaxLoginStreak { get; set; }
    public long TotalReadingTimeMinutes { get; set; }
}

public class FollowUserResponse
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsCreator { get; set; }
    public bool IsVerifiedCreator { get; set; }
    public string ReaderFearRank { get; set; } = "";
    public string? CreatorFearRank { get; set; }
    public int TotalFollowers { get; set; }
    public bool? IsFollowing { get; set; }
    public DateTime FollowedAt { get; set; }
}

// ─── Referral DTOs ────────────────────────────────────────────────────────────

public class ReferredUserInfo
{
    public string Username { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime JoinedAt { get; set; }
}

public class ReferralStatsResponse
{
    public string? ReferralCode { get; set; }
    public int TotalReferrals { get; set; }
    public int CoinsPerReferral { get; set; } = 100;
    public List<ReferredUserInfo> ReferredUsers { get; set; } = new();
}

// ─── Request DTOs ─────────────────────────────────────────────────────────────

public class UpdateProfileRequest
{
    [MinLength(3), MaxLength(50)]
    [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "Username mein sirf letters, numbers aur _ allowed hain")]
    public string? Username { get; set; }

    [MaxLength(100)]
    public string? DisplayName { get; set; }

    [MaxLength(500)]
    public string? Bio { get; set; }

    [MaxLength(15)]
    public string? Phone { get; set; }

    public string? Gender { get; set; } // male/female/other/prefer_not_to_say

    public DateTime? DateOfBirth { get; set; }

    [MaxLength(100)]
    public string? State { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(10)]
    public string? Pincode { get; set; }

    public string? PreferredLanguage { get; set; } // hindi/hinglish/english
}
public class RegisterDeviceRequest
{
    public string DeviceToken { get; set; } = "";
    public string DeviceType { get; set; } = "android";
    public string? AppVersion { get; set; }
}
