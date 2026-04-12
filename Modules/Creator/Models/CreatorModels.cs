namespace HauntedVoiceUniverse.Modules.Creator.Models;

public class CreatorStatsResponse
{
    // ── Wallet / Earnings ────────────────────────────────────────────────────
    public long TotalEarningsCoins { get; set; }
    public decimal TotalEarningsInr { get; set; }
    public long ThisMonthEarningsCoins { get; set; }
    public decimal ThisMonthEarningsInr { get; set; }
    public long WalletBalance { get; set; }
    public long PendingWithdrawalCoins { get; set; }
    public bool CanWithdraw { get; set; }
    public long MinWithdrawalCoins { get; set; } = 500;

    // ── Revenue Breakdown ────────────────────────────────────────────────────
    public long AppreciationEarnings { get; set; }
    public long LeaderboardRewards { get; set; }
    public long CompetitionPrizes { get; set; }
    public long ReferralBonus { get; set; }
    public long SignupBonus { get; set; }
    public long AdminCredits { get; set; }

    // ── Creator Tier (based on monthly views) ────────────────────────────────
    public string CreatorTier { get; set; } = "Bronze";
    public string CreatorTierIcon { get; set; } = "🥉";
    public int CreatorSharePercentage { get; set; } = 40;
    public long ThisMonthViews { get; set; }
    public long NextTierViews { get; set; }
    public double TierProgress { get; set; }

    // ── Fear Rank ─────────────────────────────────────────────────────────────
    public string FearRank { get; set; } = "Rookie Haunter";
    public string FearRankIcon { get; set; } = "👤";
    public int FearRankLevel { get; set; } = 1;
    public long FearScore { get; set; }
    public long CurrentRankMinScore { get; set; }
    public long NextRankScore { get; set; }
    public string? NextRankName { get; set; }
    public double MilestoneProgress { get; set; }

    // ── Revenue Breakdown (extended) ─────────────────────────────────────────
    // BUG#M5-3 FIX: PremiumUnlockEarnings was missing entirely from this response.
    public long PremiumUnlockEarnings { get; set; }

    // ── Content Stats ─────────────────────────────────────────────────────────
    public int StoriesCount { get; set; }
    public int PublishedStoriesCount { get; set; }
    public int TotalEpisodesCount { get; set; }
    public long TotalViews { get; set; }
    public long TotalLikes { get; set; }
    // BUG#M5-4 FIX: TotalComments was missing — Flutter was always showing 0 for Comments.
    public long TotalComments { get; set; }

    // ── Social ────────────────────────────────────────────────────────────────
    public int FollowersCount { get; set; }
    public int FollowingCount { get; set; }

    // ── Top Story ─────────────────────────────────────────────────────────────
    public TopStoryInfo? TopStory { get; set; }
}

public class TopStoryInfo
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? Slug { get; set; }
    public long Views { get; set; }
    public long Likes { get; set; }
    public string? ThumbnailUrl { get; set; }
}

// ── Earnings Hub ──────────────────────────────────────────────────────────────

public class EarningsSourceSummary
{
    public string Source { get; set; } = "";
    public long TotalCoins { get; set; }
    public decimal TotalInr { get; set; }
    public long ThisMonthCoins { get; set; }
    public decimal ThisMonthInr { get; set; }
}

public class EarningsHubResponse
{
    public long TotalCoins { get; set; }
    public decimal TotalInr { get; set; }
    public long ThisMonthCoins { get; set; }
    public decimal ThisMonthInr { get; set; }
    public List<EarningsSourceSummary> Breakdown { get; set; } = new();
}

// ── Premium Unlock Detail ─────────────────────────────────────────────────────

public class PremiumUnlockEpisodeDetail
{
    public Guid EpisodeId { get; set; }
    public string EpisodeTitle { get; set; } = "";
    public int EpisodeNumber { get; set; }
    public int CoinCost { get; set; }
    public int UnlockCount { get; set; }
    public long TotalCoins { get; set; }
    public decimal TotalInr { get; set; }
}

public class PremiumUnlockStoryDetail
{
    public Guid StoryId { get; set; }
    public string StoryTitle { get; set; } = "";
    public string? ThumbnailUrl { get; set; }
    public long TotalCoins { get; set; }
    public decimal TotalInr { get; set; }
    public List<PremiumUnlockEpisodeDetail> Episodes { get; set; } = new();
}

// ── Appreciation Detail ───────────────────────────────────────────────────────

public class AppreciationDetailItem
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public long TotalCoins { get; set; }
    public decimal TotalInr { get; set; }
    public int Count { get; set; }
    public DateTime? LastAt { get; set; }
}

// ── Super Chat Detail ─────────────────────────────────────────────────────────

public class SuperChatDetailItem
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public long Coins { get; set; }
    public decimal Inr { get; set; }
    public string? Message { get; set; }
    public string? RoomName { get; set; }
    public DateTime? SentAt { get; set; }
}

// ── Competition Detail ────────────────────────────────────────────────────────

public class CompetitionEarningDetail
{
    public string CompetitionId { get; set; } = "";
    public string CompetitionName { get; set; } = "";
    public string? Description { get; set; }
    public int Position { get; set; }
    public long CoinsEarned { get; set; }
    public decimal InrEarned { get; set; }
    public DateTime? RewardedAt { get; set; }
    public string? StoryTitle { get; set; }
}

// ── Referral Detail ───────────────────────────────────────────────────────────

public class ReferralEarningDetail
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime? ReferredAt { get; set; }
    public long CoinsEarned { get; set; }
    public decimal InrEarned { get; set; }
    public string Status { get; set; } = "active";
}

// ── Chart Data ────────────────────────────────────────────────────────────────

public class EarningsChartPoint
{
    public string Label { get; set; } = "";
    public double Coins { get; set; }
    public double Inr { get; set; }
    public string? DateKey { get; set; }
}

public class EarningsChartResponse
{
    public long TotalCoins { get; set; }
    public decimal TotalInr { get; set; }
    public List<EarningsChartPoint> Points { get; set; } = new();
}
