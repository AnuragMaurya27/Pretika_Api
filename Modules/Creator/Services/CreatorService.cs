using HauntedVoiceUniverse.Infrastructure.Database;
using HauntedVoiceUniverse.Modules.Creator.Models;

namespace HauntedVoiceUniverse.Modules.Creator.Services;

public interface ICreatorService
{
    Task<(bool Success, string Message, CreatorStatsResponse? Data)> GetStatsAsync(Guid userId);
    Task<EarningsHubResponse> GetEarningsHubAsync(Guid userId);
    Task<List<PremiumUnlockStoryDetail>> GetPremiumUnlockEarningsAsync(Guid userId);
    Task<(List<AppreciationDetailItem> Items, int Total)> GetAppreciationEarningsAsync(Guid userId, int page, int pageSize);
    Task<(List<SuperChatDetailItem> Items, int Total)> GetSuperChatEarningsAsync(Guid userId, int page, int pageSize);
    Task<List<CompetitionEarningDetail>> GetCompetitionEarningsAsync(Guid userId);
    Task<List<ReferralEarningDetail>> GetReferralEarningsAsync(Guid userId);
    Task<EarningsChartResponse> GetEarningsChartAsync(Guid userId, string period, DateTime from, DateTime to);
}

public class CreatorService : ICreatorService
{
    private readonly IDbConnectionFactory _db;

    // Conversion rate: 1 coin = ₹0.10
    private const decimal CoinToInr = 0.10m;
    private const long MinWithdrawal = 500;

    public CreatorService(IDbConnectionFactory db)
    {
        _db = db;
    }

    // ── Fear Rank Logic ──────────────────────────────────────────────────────
    // BUG#M6-1 FIX: Ranks were 7 English-named ranks ("Rookie Haunter" etc.) but
    // Flutter rank_constants.dart defines 6 Hindi named ranks. They must match exactly
    // so creatorRankMeta(key) lookups succeed in Flutter. Also added snake_case Key field.
    private static readonly (long MinScore, long NextScore, string Name, string Key, string Icon, int Level)[] FearRanks = new[]
    {
        (0L,        500L,          "Pret Aatma",            "pret_aatma",            "👻", 1),
        (500L,      2000L,         "Shraapit Lekhak",       "shraapit_lekhak",       "📜", 2),
        (2000L,     10000L,        "Andhkaar Rachnakar",    "andhkaar_rachnakar",    "🌑", 3),
        (10000L,    50000L,        "Bhoot Samrat",          "bhoot_samrat",          "👑", 4),
        (50000L,    200000L,       "Tantrik Master",        "tantrik_master",        "🔮", 5),
        (200000L,   long.MaxValue, "Mahakaal Katha Samrat", "mahakaal_katha_samrat", "💀", 6),
    };

    // BUG#M6-1 FIX: Return type now includes Key (snake_case) so Flutter can look up
    // the rank by key instead of receiving a display-name string it cannot match.
    private static (string Name, string Key, string Icon, int Level, long MinScore, long NextScore, string? NextRankName, double Progress)
        GetFearRank(long score)
    {
        for (int i = 0; i < FearRanks.Length; i++)
        {
            var r = FearRanks[i];
            if (score < r.NextScore || i == FearRanks.Length - 1)
            {
                string? nextName = (i + 1 < FearRanks.Length) ? FearRanks[i + 1].Name : null;
                double progress = r.NextScore == long.MaxValue ? 100.0
                    : Math.Min(100.0, (score - r.MinScore) * 100.0 / (r.NextScore - r.MinScore));
                return (r.Name, r.Key, r.Icon, r.Level, r.MinScore, r.NextScore, nextName, progress);
            }
        }
        return ("Mahakaal Katha Samrat", "mahakaal_katha_samrat", "💀", 6, 200000L, long.MaxValue, null, 100.0);
    }

    // ── Creator Tier Logic ───────────────────────────────────────────────────
    private static (string Tier, string Icon, int Share, long NextViews, double Progress)
        GetCreatorTier(long monthlyViews)
    {
        if (monthlyViews < 10000)
            return ("Bronze", "🥉", 40, 10000, monthlyViews * 100.0 / 10000);
        if (monthlyViews < 50000)
            return ("Silver", "🥈", 45, 50000, (monthlyViews - 10000) * 100.0 / 40000);
        if (monthlyViews < 100000)
            return ("Gold", "🥇", 50, 100000, (monthlyViews - 50000) * 100.0 / 50000);
        return ("Platinum", "💎", 55, long.MaxValue, 100.0);
    }

    // ── Main Stats Query ─────────────────────────────────────────────────────
    public async Task<(bool Success, string Message, CreatorStatsResponse? Data)> GetStatsAsync(Guid userId)
    {
        // BUG#M5-5 FIX: Use await using for proper async disposal of NpgsqlConnection.
        await using var conn = await _db.CreateConnectionAsync();

        // 1. Story stats
        // BUG#M5-1 FIX: Added deleted_at IS NULL — soft-deleted stories must not count
        // in any aggregated stats. Without this, deleted content inflated totals.
        var storyStats = await DbHelper.ExecuteReaderFirstAsync(conn,
            @"SELECT
                COUNT(*)                                     AS total_stories,
                COUNT(*) FILTER (WHERE status = 'published') AS published_stories,
                COALESCE(SUM(total_views), 0)               AS total_views,
                COALESCE(SUM(total_likes), 0)               AS total_likes,
                COALESCE(SUM(total_episodes), 0)            AS total_episodes,
                COALESCE(SUM(total_comments), 0)            AS total_comments
              FROM stories
              WHERE creator_id = @uid AND deleted_at IS NULL",
            r => new
            {
                TotalStories    = (int)DbHelper.GetLong(r, "total_stories"),
                Published       = (int)DbHelper.GetLong(r, "published_stories"),
                TotalViews      = DbHelper.GetLong(r, "total_views"),
                TotalLikes      = DbHelper.GetLong(r, "total_likes"),
                TotalEpisodes   = (int)DbHelper.GetLong(r, "total_episodes"),
                TotalComments   = DbHelper.GetLong(r, "total_comments"),
            },
            new Dictionary<string, object?> { ["uid"] = userId });

        // 2. Top story
        // BUG#M5-1 FIX: Also guard top story against deleted stories.
        var topStory = await DbHelper.ExecuteReaderFirstAsync(conn,
            @"SELECT id, title, slug, total_views, total_likes, thumbnail_url
              FROM stories
              WHERE creator_id = @uid AND status = 'published' AND deleted_at IS NULL
              ORDER BY total_views DESC
              LIMIT 1",
            r => new TopStoryInfo
            {
                Id           = DbHelper.GetGuid(r, "id"),
                Title        = DbHelper.GetString(r, "title"),
                Slug         = DbHelper.GetStringOrNull(r, "slug"),
                Views        = DbHelper.GetLong(r, "total_views"),
                Likes        = DbHelper.GetLong(r, "total_likes"),
                ThumbnailUrl = DbHelper.GetStringOrNull(r, "thumbnail_url"),
            },
            new Dictionary<string, object?> { ["uid"] = userId });

        // 3. Social (followers / following)
        var followers = await DbHelper.ExecuteScalarAsync<long>(conn,
            "SELECT COUNT(*) FROM follows WHERE following_id = @uid",
            new Dictionary<string, object?> { ["uid"] = userId });

        var following = await DbHelper.ExecuteScalarAsync<long>(conn,
            "SELECT COUNT(*) FROM follows WHERE follower_id = @uid",
            new Dictionary<string, object?> { ["uid"] = userId });

        // 4. Wallet
        var wallet = await DbHelper.ExecuteReaderFirstAsync(conn,
            "SELECT coin_balance, pending_withdrawal FROM wallets WHERE user_id = @uid",
            r => new
            {
                Balance            = DbHelper.GetLong(r, "coin_balance"),
                PendingWithdrawal  = DbHelper.GetLong(r, "pending_withdrawal"),
            },
            new Dictionary<string, object?> { ["uid"] = userId });

        // 5. Lifetime earnings breakdown from coin_transactions
        var earnings = await DbHelper.ExecuteReaderFirstAsync(conn,
            @"SELECT
                COALESCE(SUM(CASE WHEN transaction_type = 'appreciation'
                                  THEN COALESCE(creator_share, 0) ELSE 0 END), 0)  AS appreciation,
                COALESCE(SUM(CASE WHEN transaction_type = 'leaderboard_reward'
                                  THEN amount ELSE 0 END), 0)                      AS leaderboard,
                COALESCE(SUM(CASE WHEN transaction_type = 'competition_reward'
                                  THEN amount ELSE 0 END), 0)                      AS competition,
                COALESCE(SUM(CASE WHEN transaction_type = 'referral_bonus'
                                  THEN amount ELSE 0 END), 0)                      AS referral,
                COALESCE(SUM(CASE WHEN transaction_type = 'signup_bonus'
                                  THEN amount ELSE 0 END), 0)                      AS signup_bonus,
                COALESCE(SUM(CASE WHEN transaction_type = 'admin_credit'
                                  THEN amount ELSE 0 END), 0)                      AS admin_credits
              FROM coin_transactions
              WHERE receiver_id = @uid AND status = 'completed'",
            r => new
            {
                Appreciation  = DbHelper.GetLong(r, "appreciation"),
                Leaderboard   = DbHelper.GetLong(r, "leaderboard"),
                Competition   = DbHelper.GetLong(r, "competition"),
                Referral      = DbHelper.GetLong(r, "referral"),
                SignupBonus   = DbHelper.GetLong(r, "signup_bonus"),
                AdminCredits  = DbHelper.GetLong(r, "admin_credits"),
            },
            new Dictionary<string, object?> { ["uid"] = userId });

        // 6. This-month earnings
        // BUG#M5-2 FIX: Use IST (Asia/Kolkata = UTC+5:30) month boundary, not UTC.
        // date_trunc('month', NOW()) resets at 18:30 UTC = wrong month for Indian creators.
        var thisMonthEarnings = await DbHelper.ExecuteScalarAsync<long>(conn,
            @"SELECT COALESCE(SUM(
                CASE
                  WHEN transaction_type = 'appreciation' THEN COALESCE(creator_share, 0)
                  WHEN transaction_type NOT IN ('recharge') THEN amount
                  ELSE 0
                END
              ), 0)
              FROM coin_transactions
              WHERE receiver_id = @uid AND status = 'completed'
                AND transaction_type NOT IN ('recharge')
                AND date_trunc('month', COALESCE(completed_at, created_at) AT TIME ZONE 'Asia/Kolkata')
                  = date_trunc('month', NOW() AT TIME ZONE 'Asia/Kolkata')",
            new Dictionary<string, object?> { ["uid"] = userId });

        // BUG#M5-3 FIX: Premium unlock revenue was completely missing from stats.
        // episode_unlocks.coins_spent holds what readers paid for locked episodes
        // and this never appeared in coin_transactions for the creator.
        var premiumUnlockEarnings = await DbHelper.ExecuteReaderFirstAsync(conn,
            @"SELECT
                COALESCE(SUM(eu.coins_spent), 0)::bigint AS total_coins,
                COALESCE(SUM(CASE
                  WHEN date_trunc('month', eu.unlocked_at AT TIME ZONE 'Asia/Kolkata')
                     = date_trunc('month', NOW() AT TIME ZONE 'Asia/Kolkata')
                  THEN eu.coins_spent ELSE 0 END), 0)::bigint AS this_month_coins
              FROM episode_unlocks eu
              JOIN episodes e ON e.id = eu.episode_id AND e.deleted_at IS NULL
              JOIN stories s  ON s.id  = e.story_id  AND s.deleted_at IS NULL
              WHERE s.creator_id = @uid",
            r => new
            {
                Total      = DbHelper.GetLong(r, "total_coins"),
                ThisMonth  = DbHelper.GetLong(r, "this_month_coins"),
            },
            new Dictionary<string, object?> { ["uid"] = userId });

        // 7. Monthly views + unique readers (from story_views joined to creator's stories)
        long thisMonthViews = 0;
        long uniqueReaders = 0;
        try
        {
            // BUG#M5-2 FIX: IST month boundary for monthly views
            thisMonthViews = await DbHelper.ExecuteScalarAsync<long>(conn,
                @"SELECT COUNT(*)
                  FROM story_views sv
                  JOIN stories s ON s.id = sv.story_id AND s.deleted_at IS NULL
                  WHERE s.creator_id = @uid
                    AND sv.viewed_at >= date_trunc('month', NOW() AT TIME ZONE 'Asia/Kolkata') AT TIME ZONE 'Asia/Kolkata'",
                new Dictionary<string, object?> { ["uid"] = userId });

            // BUG#M6-2 FIX: Unique readers needed for 7-factor fear score algorithm.
            // COUNT DISTINCT authenticated readers across all creator's published stories.
            uniqueReaders = await DbHelper.ExecuteScalarAsync<long>(conn,
                @"SELECT COUNT(DISTINCT sv.user_id)
                  FROM story_views sv
                  JOIN stories s ON s.id = sv.story_id AND s.deleted_at IS NULL
                  WHERE s.creator_id = @uid AND sv.user_id IS NOT NULL",
                new Dictionary<string, object?> { ["uid"] = userId });
        }
        catch { /* story_views table might not exist in some environments */ }

        // ── Compute derived values ────────────────────────────────────────────
        long totalViews    = storyStats?.TotalViews    ?? 0;
        long totalLikes    = storyStats?.TotalLikes    ?? 0;
        long totalComments = storyStats?.TotalComments ?? 0;

        // Coins received from readers (appreciation + super_chat)
        long coinsReceived = (earnings?.Appreciation ?? 0)
                           + await DbHelper.ExecuteScalarAsync<long>(conn,
                               @"SELECT COALESCE(SUM(amount), 0)
                                 FROM coin_transactions
                                 WHERE receiver_id = @uid AND status = 'completed'
                                   AND transaction_type = 'super_chat'",
                               new Dictionary<string, object?> { ["uid"] = userId });

        // BUG#M6-2 FIX: Implement the 7-factor weighted fear score algorithm:
        //   Views 30% + Unique Readers 15% + Comments 15% + Avg Read Time 10% +
        //   Reactions 10% + Coins Received 10% + Completion Rate 10% = 100%
        //
        // Raw weights per unit (calibrated so thresholds Pret→Mahakaal span
        // realistic creator sizes):
        //   views × 0.30, uniqueReaders × 1.5, comments × 1.5,
        //   reactions(likes) × 1.0, coinsReceived × 1.0
        //
        // TODO: avgReadTimeMinutes (10%) and completionRate (10%) require a
        //   read_sessions / completion_events table that does not yet exist.
        //   Their 20% weight is intentionally omitted until schema is extended.
        //   The remaining 80% factors still produce a fair relative ranking.
        long fearScore = (long)(
            totalViews    * 0.30 +
            uniqueReaders * 1.50 +
            totalComments * 1.50 +
            totalLikes    * 1.00 +
            coinsReceived * 1.00
        );

        var (rankName, rankKey, rankIcon, rankLevel, rankMin, rankNext, nextRankName, rankProgress) = GetFearRank(fearScore);
        var (tier, tierIcon, tierShare, nextTierViews, tierProgress) = GetCreatorTier(thisMonthViews);

        // BUG#M5-3 FIX: Include premium unlock earnings in totals.
        long premiumTotal      = premiumUnlockEarnings?.Total     ?? 0;
        long premiumThisMonth  = premiumUnlockEarnings?.ThisMonth ?? 0;

        long totalEarnings = (earnings?.Appreciation ?? 0)
                           + (earnings?.Leaderboard  ?? 0)
                           + (earnings?.Competition  ?? 0)
                           + (earnings?.Referral     ?? 0)
                           + (earnings?.AdminCredits ?? 0)
                           + premiumTotal;
        // Note: signup_bonus is for the user personally, not creator revenue — excluded from total

        var response = new CreatorStatsResponse
        {
            // Earnings — BUG#M5-3 FIX: premium unlock now included
            TotalEarningsCoins      = totalEarnings,
            TotalEarningsInr        = totalEarnings * CoinToInr,
            ThisMonthEarningsCoins  = thisMonthEarnings + premiumThisMonth,
            ThisMonthEarningsInr    = (thisMonthEarnings + premiumThisMonth) * CoinToInr,
            PremiumUnlockEarnings   = premiumTotal,
            WalletBalance           = wallet?.Balance ?? 0,
            PendingWithdrawalCoins  = wallet?.PendingWithdrawal ?? 0,
            CanWithdraw             = (wallet?.Balance ?? 0) >= MinWithdrawal,
            MinWithdrawalCoins      = MinWithdrawal,

            // Revenue breakdown
            AppreciationEarnings = earnings?.Appreciation ?? 0,
            LeaderboardRewards   = earnings?.Leaderboard  ?? 0,
            CompetitionPrizes    = earnings?.Competition  ?? 0,
            ReferralBonus        = earnings?.Referral     ?? 0,
            SignupBonus          = earnings?.SignupBonus  ?? 0,
            AdminCredits         = earnings?.AdminCredits ?? 0,

            // Creator Tier
            CreatorTier           = tier,
            CreatorTierIcon       = tierIcon,
            CreatorSharePercentage = tierShare,
            ThisMonthViews        = thisMonthViews,
            NextTierViews         = nextTierViews == long.MaxValue ? 0 : nextTierViews,
            TierProgress          = Math.Min(100.0, tierProgress),

            // Fear Rank
            // BUG#M6-1 FIX: Return snake_case key ("pret_aatma") not display name ("Pret Aatma")
            // Flutter's FearRanks.creatorRankMeta() looks up by key — display name lookup always
            // fell back to creatorRanks.first (Pret Aatma), freezing all creators at rank 1.
            FearRank             = rankKey,
            FearRankIcon         = rankIcon,
            FearRankLevel        = rankLevel,
            FearScore            = fearScore,
            CurrentRankMinScore  = rankMin,
            NextRankScore        = rankNext == long.MaxValue ? 0 : rankNext,
            NextRankName         = nextRankName,
            MilestoneProgress    = Math.Min(100.0, rankProgress),

            // Content — BUG#M5-4 FIX: total_comments now included
            StoriesCount         = storyStats?.TotalStories ?? 0,
            PublishedStoriesCount = storyStats?.Published    ?? 0,
            TotalEpisodesCount   = storyStats?.TotalEpisodes ?? 0,
            TotalViews           = totalViews,
            TotalLikes           = totalLikes,
            TotalComments        = storyStats?.TotalComments ?? 0,

            // Social
            FollowersCount = (int)followers,
            FollowingCount = (int)following,

            // Top Story
            TopStory = topStory,
        };

        return (true, "Stats taiyaar hain", response);
    }

    // ── Earnings Hub ─────────────────────────────────────────────────────────

    public async Task<EarningsHubResponse> GetEarningsHubAsync(Guid userId)
    {
        // BUG#M5-5 FIX: Use await using for proper async disposal of NpgsqlConnection.
        await using var conn = await _db.CreateConnectionAsync();

        // Per-source breakdown using UNION
        // BUG#M5-2 FIX: Use IST month boundary for "this month" aggregation.
        var rows = await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT source,
                     SUM(coins)::bigint AS total_coins,
                     SUM(coins * 0.10)  AS total_inr,
                     SUM(CASE WHEN date_trunc('month', ts AT TIME ZONE 'Asia/Kolkata') = date_trunc('month', NOW() AT TIME ZONE 'Asia/Kolkata')
                              THEN coins ELSE 0 END)::bigint AS this_month_coins,
                     SUM(CASE WHEN date_trunc('month', ts AT TIME ZONE 'Asia/Kolkata') = date_trunc('month', NOW() AT TIME ZONE 'Asia/Kolkata')
                              THEN coins * 0.10 ELSE 0 END)  AS this_month_inr
              FROM (
                SELECT 'appreciation' AS source,
                       COALESCE(creator_share, amount) AS coins,
                       COALESCE(completed_at, created_at) AS ts
                FROM coin_transactions
                WHERE receiver_id = @uid AND transaction_type = 'appreciation' AND status = 'completed'
                UNION ALL
                SELECT 'super_chat', amount, COALESCE(completed_at, created_at)
                FROM coin_transactions
                WHERE receiver_id = @uid AND transaction_type = 'super_chat' AND status = 'completed'
                UNION ALL
                SELECT 'competition', amount, COALESCE(completed_at, created_at)
                FROM coin_transactions
                WHERE receiver_id = @uid
                  AND transaction_type IN ('competition_reward', 'leaderboard_reward')
                  AND status = 'completed'
                UNION ALL
                SELECT 'referral', amount, COALESCE(completed_at, created_at)
                FROM coin_transactions
                WHERE receiver_id = @uid AND transaction_type = 'referral_bonus' AND status = 'completed'
                UNION ALL
                SELECT 'premium_unlock', eu.coins_spent, eu.unlocked_at
                FROM episode_unlocks eu
                JOIN episodes e ON e.id = eu.episode_id AND e.deleted_at IS NULL
                JOIN stories s ON s.id = e.story_id AND s.deleted_at IS NULL
                WHERE s.creator_id = @uid
              ) t
              GROUP BY source",
            r => new EarningsSourceSummary
            {
                Source         = DbHelper.GetString(r, "source"),
                TotalCoins     = DbHelper.GetLong(r, "total_coins"),
                TotalInr       = DbHelper.GetDecimal(r, "total_inr"),
                ThisMonthCoins = DbHelper.GetLong(r, "this_month_coins"),
                ThisMonthInr   = DbHelper.GetDecimal(r, "this_month_inr"),
            },
            new Dictionary<string, object?> { ["uid"] = userId });

        var breakdown = rows ?? new List<EarningsSourceSummary>();
        return new EarningsHubResponse
        {
            TotalCoins     = breakdown.Sum(x => x.TotalCoins),
            TotalInr       = breakdown.Sum(x => x.TotalInr),
            ThisMonthCoins = breakdown.Sum(x => x.ThisMonthCoins),
            ThisMonthInr   = breakdown.Sum(x => x.ThisMonthInr),
            Breakdown      = breakdown,
        };
    }

    // ── Premium Unlock Detail ─────────────────────────────────────────────────

    public async Task<List<PremiumUnlockStoryDetail>> GetPremiumUnlockEarningsAsync(Guid userId)
    {
        // BUG#M5-5 FIX: Use await using for proper async disposal of NpgsqlConnection.
        await using var conn = await _db.CreateConnectionAsync();

        var rows = await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT s.id AS story_id, s.title AS story_title, s.thumbnail_url,
                     e.id AS episode_id, e.title AS episode_title,
                     e.episode_number, e.unlock_coin_cost AS coin_cost,
                     COUNT(eu.id)::int   AS unlock_count,
                     SUM(eu.coins_spent)::bigint AS total_coins
              FROM episodes e
              JOIN stories s ON s.id = e.story_id AND s.creator_id = @uid AND s.deleted_at IS NULL
              JOIN episode_unlocks eu ON eu.episode_id = e.id
              WHERE e.deleted_at IS NULL
              GROUP BY s.id, s.title, s.thumbnail_url, e.id, e.title, e.episode_number, e.unlock_coin_cost
              ORDER BY s.title, e.episode_number",
            r => new
            {
                StoryId       = DbHelper.GetGuid(r, "story_id"),
                StoryTitle    = DbHelper.GetString(r, "story_title"),
                ThumbnailUrl  = DbHelper.GetStringOrNull(r, "thumbnail_url"),
                EpisodeId     = DbHelper.GetGuid(r, "episode_id"),
                EpisodeTitle  = DbHelper.GetString(r, "episode_title"),
                EpisodeNumber = DbHelper.GetInt(r, "episode_number"),
                CoinCost      = DbHelper.GetInt(r, "coin_cost"),
                UnlockCount   = DbHelper.GetInt(r, "unlock_count"),
                TotalCoins    = DbHelper.GetLong(r, "total_coins"),
            },
            new Dictionary<string, object?> { ["uid"] = userId });

        // Group by story
        var stories = new Dictionary<Guid, PremiumUnlockStoryDetail>();
        foreach (var row in rows ?? new())
        {
            if (!stories.TryGetValue(row.StoryId, out var story))
            {
                story = new PremiumUnlockStoryDetail
                {
                    StoryId      = row.StoryId,
                    StoryTitle   = row.StoryTitle,
                    ThumbnailUrl = row.ThumbnailUrl,
                };
                stories[row.StoryId] = story;
            }
            var ep = new PremiumUnlockEpisodeDetail
            {
                EpisodeId     = row.EpisodeId,
                EpisodeTitle  = row.EpisodeTitle,
                EpisodeNumber = row.EpisodeNumber,
                CoinCost      = row.CoinCost,
                UnlockCount   = row.UnlockCount,
                TotalCoins    = row.TotalCoins,
                TotalInr      = row.TotalCoins * CoinToInr,
            };
            story.Episodes.Add(ep);
            story.TotalCoins += ep.TotalCoins;
            story.TotalInr   += ep.TotalInr;
        }
        return stories.Values.ToList();
    }

    // ── Appreciation Detail ───────────────────────────────────────────────────

    public async Task<(List<AppreciationDetailItem> Items, int Total)> GetAppreciationEarningsAsync(
        Guid userId, int page, int pageSize)
    {
        // BUG#M5-5 FIX: Use await using for proper async disposal of NpgsqlConnection.
        await using var conn = await _db.CreateConnectionAsync();

        var total = (int)await DbHelper.ExecuteScalarAsync<long>(conn,
            @"SELECT COUNT(DISTINCT sender_id) FROM coin_transactions
              WHERE receiver_id = @uid AND transaction_type = 'appreciation' AND status = 'completed'",
            new Dictionary<string, object?> { ["uid"] = userId });

        var items = await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT u.id AS user_id, u.username, u.display_name, u.avatar_url,
                     SUM(COALESCE(ct.creator_share, ct.amount))::bigint AS total_coins,
                     COUNT(*)::int AS cnt,
                     MAX(COALESCE(ct.completed_at, ct.created_at)) AS last_at
              FROM coin_transactions ct
              JOIN users u ON u.id = ct.sender_id
              WHERE ct.receiver_id = @uid AND ct.transaction_type = 'appreciation' AND ct.status = 'completed'
              GROUP BY u.id, u.username, u.display_name, u.avatar_url
              ORDER BY total_coins DESC
              LIMIT @limit OFFSET @offset",
            r => new AppreciationDetailItem
            {
                UserId      = DbHelper.GetGuid(r, "user_id"),
                Username    = DbHelper.GetString(r, "username"),
                DisplayName = DbHelper.GetStringOrNull(r, "display_name"),
                AvatarUrl   = DbHelper.GetStringOrNull(r, "avatar_url"),
                TotalCoins  = DbHelper.GetLong(r, "total_coins"),
                TotalInr    = DbHelper.GetLong(r, "total_coins") * CoinToInr,
                Count       = DbHelper.GetInt(r, "cnt"),
                LastAt      = DbHelper.GetDateTimeOrNull(r, "last_at"),
            },
            new Dictionary<string, object?>
            {
                ["uid"] = userId,
                ["limit"] = pageSize,
                ["offset"] = (page - 1) * pageSize,
            });

        return (items ?? new(), total);
    }

    // ── Super Chat Detail ─────────────────────────────────────────────────────

    public async Task<(List<SuperChatDetailItem> Items, int Total)> GetSuperChatEarningsAsync(
        Guid userId, int page, int pageSize)
    {
        // BUG#M5-5 FIX: Use await using for proper async disposal of NpgsqlConnection.
        await using var conn = await _db.CreateConnectionAsync();

        // Super chats received — tracked in coin_transactions with receiver_id
        var total = (int)await DbHelper.ExecuteScalarAsync<long>(conn,
            @"SELECT COUNT(*) FROM coin_transactions
              WHERE receiver_id = @uid AND transaction_type = 'super_chat' AND status = 'completed'",
            new Dictionary<string, object?> { ["uid"] = userId });

        var items = await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT u.id AS user_id, u.username, u.display_name, u.avatar_url,
                     ct.amount AS coins,
                     ct.description AS message,
                     NULL::text AS room_name,
                     COALESCE(ct.completed_at, ct.created_at) AS sent_at
              FROM coin_transactions ct
              JOIN users u ON u.id = ct.sender_id
              WHERE ct.receiver_id = @uid AND ct.transaction_type = 'super_chat' AND ct.status = 'completed'
              ORDER BY sent_at DESC
              LIMIT @limit OFFSET @offset",
            r => new SuperChatDetailItem
            {
                UserId      = DbHelper.GetGuid(r, "user_id"),
                Username    = DbHelper.GetString(r, "username"),
                DisplayName = DbHelper.GetStringOrNull(r, "display_name"),
                AvatarUrl   = DbHelper.GetStringOrNull(r, "avatar_url"),
                Coins       = DbHelper.GetLong(r, "coins"),
                Inr         = DbHelper.GetLong(r, "coins") * CoinToInr,
                Message     = DbHelper.GetStringOrNull(r, "message"),
                RoomName    = DbHelper.GetStringOrNull(r, "room_name"),
                SentAt      = DbHelper.GetDateTimeOrNull(r, "sent_at"),
            },
            new Dictionary<string, object?>
            {
                ["uid"] = userId,
                ["limit"] = pageSize,
                ["offset"] = (page - 1) * pageSize,
            });

        return (items ?? new(), total);
    }

    // ── Competition Detail ────────────────────────────────────────────────────

    public async Task<List<CompetitionEarningDetail>> GetCompetitionEarningsAsync(Guid userId)
    {
        // BUG#M5-5 FIX: Use await using for proper async disposal of NpgsqlConnection.
        await using var conn = await _db.CreateConnectionAsync();

        var rows = await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT ct.id::text AS comp_id, ct.description AS comp_name,
                     ct.amount AS coins_earned,
                     COALESCE(ct.completed_at, ct.created_at) AS rewarded_at,
                     NULL::text AS story_title
              FROM coin_transactions ct
              WHERE ct.receiver_id = @uid
                AND ct.transaction_type IN ('competition_reward', 'leaderboard_reward')
                AND ct.status = 'completed'
              ORDER BY rewarded_at DESC",
            r => new CompetitionEarningDetail
            {
                CompetitionId   = DbHelper.GetString(r, "comp_id"),
                CompetitionName = DbHelper.GetString(r, "comp_name"),
                Position        = 0,
                CoinsEarned     = DbHelper.GetLong(r, "coins_earned"),
                InrEarned       = DbHelper.GetLong(r, "coins_earned") * CoinToInr,
                RewardedAt      = DbHelper.GetDateTimeOrNull(r, "rewarded_at"),
                StoryTitle      = DbHelper.GetStringOrNull(r, "story_title"),
            },
            new Dictionary<string, object?> { ["uid"] = userId });

        return rows ?? new();
    }

    // ── Referral Detail ───────────────────────────────────────────────────────

    public async Task<List<ReferralEarningDetail>> GetReferralEarningsAsync(Guid userId)
    {
        // BUG#M5-5 FIX: Use await using for proper async disposal of NpgsqlConnection.
        await using var conn = await _db.CreateConnectionAsync();

        var rows = await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT u.id AS user_id, u.username, u.display_name, u.avatar_url,
                     u.created_at AS referred_at, u.status,
                     ct.amount AS coins_earned
              FROM users u
              JOIN coin_transactions ct ON ct.receiver_id = @uid
                AND ct.transaction_type = 'referral_bonus'
                AND ct.status = 'completed'
                AND ct.reference_id = u.id
              WHERE u.referred_by = @uid
              ORDER BY u.created_at DESC",
            r => new ReferralEarningDetail
            {
                UserId      = DbHelper.GetGuid(r, "user_id"),
                Username    = DbHelper.GetString(r, "username"),
                DisplayName = DbHelper.GetStringOrNull(r, "display_name"),
                AvatarUrl   = DbHelper.GetStringOrNull(r, "avatar_url"),
                ReferredAt  = DbHelper.GetDateTimeOrNull(r, "referred_at"),
                CoinsEarned = DbHelper.GetLong(r, "coins_earned"),
                InrEarned   = DbHelper.GetLong(r, "coins_earned") * CoinToInr,
                Status      = DbHelper.GetString(r, "status"),
            },
            new Dictionary<string, object?> { ["uid"] = userId });

        return rows ?? new();
    }

    // ── Earnings Chart ────────────────────────────────────────────────────────

    public async Task<EarningsChartResponse> GetEarningsChartAsync(
        Guid userId, string period, DateTime from, DateTime to)
    {
        // BUG#M5-5 FIX: Use await using for proper async disposal of NpgsqlConnection.
        await using var conn = await _db.CreateConnectionAsync();

        var (truncFn, labelFmt) = period.ToLower() switch
        {
            "monthly" => ("month", "Mon YYYY"),
            "yearly"  => ("year",  "YYYY"),
            _         => ("day",   "DD Mon"),
        };

        var rows = await DbHelper.ExecuteReaderAsync(conn,
            $@"SELECT date_trunc('{truncFn}', ts)::date AS period_date,
                      SUM(coins)::bigint              AS coins,
                      SUM(coins * 0.10)               AS inr
               FROM (
                 SELECT COALESCE(creator_share, amount) AS coins,
                        COALESCE(completed_at, created_at) AS ts
                 FROM coin_transactions
                 WHERE receiver_id = @uid AND status = 'completed'
                   AND transaction_type IN ('appreciation','super_chat','competition_reward','leaderboard_reward','referral_bonus')
                   AND COALESCE(completed_at, created_at) BETWEEN @from AND @to
                 UNION ALL
                 SELECT eu.coins_spent, eu.unlocked_at
                 FROM episode_unlocks eu
                 JOIN episodes e ON e.id = eu.episode_id AND e.deleted_at IS NULL
                 JOIN stories s ON s.id = e.story_id AND s.deleted_at IS NULL
                 WHERE s.creator_id = @uid
                   AND eu.unlocked_at BETWEEN @from AND @to
               ) t
               GROUP BY period_date
               ORDER BY period_date",
            r =>
            {
                var date = r.GetDateTime(r.GetOrdinal("period_date"));
                var label = period.ToLower() switch
                {
                    "monthly" => date.ToString("MMM yyyy"),
                    "yearly"  => date.ToString("yyyy"),
                    _         => date.ToString("dd MMM"),
                };
                return new EarningsChartPoint
                {
                    Label   = label,
                    Coins   = (double)DbHelper.GetLong(r, "coins"),
                    Inr     = (double)DbHelper.GetDecimal(r, "inr"),
                    DateKey = date.ToString("yyyy-MM-dd"),
                };
            },
            new Dictionary<string, object?>
            {
                ["uid"]  = userId,
                ["from"] = from.Date,
                ["to"]   = to.Date.AddDays(1).AddSeconds(-1),
            });

        var points = rows ?? new();
        return new EarningsChartResponse
        {
            TotalCoins = (long)points.Sum(p => p.Coins),
            TotalInr   = (decimal)points.Sum(p => p.Inr),
            Points     = points,
        };
    }
}
