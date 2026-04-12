using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Infrastructure.Database;
using HauntedVoiceUniverse.Modules.Leaderboard.Models;
using Npgsql;

namespace HauntedVoiceUniverse.Modules.Leaderboard.Services;

public interface ILeaderboardService
{
    Task<List<LeaderboardEntryResponse>> GetLeaderboardAsync(string type, string? periodType, Guid? viewerId);
    Task<List<CompetitionResponse>> GetCompetitionsAsync(bool activeOnly, Guid? viewerId, int page, int pageSize);
    Task<CompetitionResponse?> GetCompetitionAsync(Guid competitionId, Guid? viewerId);
    Task<PagedResult<CompetitionEntryResponse>> GetCompetitionEntriesAsync(Guid competitionId, Guid? viewerId, int page, int pageSize);
    Task<(bool Success, string Message)> SubmitEntryAsync(Guid userId, Guid competitionId, SubmitEntryRequest req);
    Task<(bool Success, string Message)> VoteForEntryAsync(Guid userId, Guid competitionId, Guid entryId);
}

public class LeaderboardService : ILeaderboardService
{
    private readonly IDbConnectionFactory _db;

    public LeaderboardService(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task<List<LeaderboardEntryResponse>> GetLeaderboardAsync(string type, string? periodType, Guid? viewerId)
    {
        // BUG#M6-4 FIX: NpgsqlConnection implements IAsyncDisposable; synchronous using
        // bypasses async teardown. Changed all 6 methods to await using.
        await using var conn = await _db.CreateConnectionAsync();

        // If leaderboard data exists, fetch from table
        var whereParts = new List<string> { "lb.leaderboard_type = @type" };
        var parameters = new Dictionary<string, object?> { ["type"] = type };

        if (!string.IsNullOrEmpty(periodType))
        {
            whereParts.Add("lb.period_type = @ptype");
            parameters["ptype"] = periodType;
        }

        var sql = $@"
            SELECT lb.id, lb.rank_position, lb.score, lb.entity_type, lb.entity_id,
                   lb.reward_coins, lb.period_start, lb.period_end,
                   -- Story fields
                   s.title as story_title, s.slug as story_slug,
                   s.thumbnail_url as story_thumbnail_url,
                   s.total_views as story_total_views, s.total_likes as story_total_likes,
                   -- Creator fields
                   u.username as creator_username, u.display_name as creator_display_name,
                   u.avatar_url as creator_avatar_url, u.is_verified_creator as creator_is_verified,
                   u.creator_fear_rank as creator_rank
            FROM leaderboards lb
            LEFT JOIN stories s ON lb.entity_type = 'story' AND s.id = lb.entity_id
            LEFT JOIN users u ON lb.entity_type = 'creator' AND u.id = lb.entity_id
            WHERE {string.Join(" AND ", whereParts)}
            ORDER BY lb.period_start DESC, lb.rank_position ASC
            LIMIT 100";

        return await DbHelper.ExecuteReaderAsync(conn, sql, r => new LeaderboardEntryResponse
        {
            Id = DbHelper.GetGuid(r, "id"),
            RankPosition = DbHelper.GetInt(r, "rank_position"),
            Score = DbHelper.GetDecimal(r, "score"),
            EntityType = DbHelper.GetString(r, "entity_type"),
            EntityId = DbHelper.GetGuid(r, "entity_id"),
            StoryTitle = DbHelper.GetStringOrNull(r, "story_title"),
            StorySlug = DbHelper.GetStringOrNull(r, "story_slug"),
            StoryThumbnailUrl = DbHelper.GetStringOrNull(r, "story_thumbnail_url"),
            StoryTotalViews = r.IsDBNull(r.GetOrdinal("story_total_views")) ? null : DbHelper.GetLong(r, "story_total_views"),
            StoryTotalLikes = r.IsDBNull(r.GetOrdinal("story_total_likes")) ? null : DbHelper.GetLong(r, "story_total_likes"),
            CreatorUsername = DbHelper.GetStringOrNull(r, "creator_username"),
            CreatorDisplayName = DbHelper.GetStringOrNull(r, "creator_display_name"),
            CreatorAvatarUrl = DbHelper.GetStringOrNull(r, "creator_avatar_url"),
            CreatorIsVerified = r.IsDBNull(r.GetOrdinal("creator_is_verified")) ? null : DbHelper.GetBool(r, "creator_is_verified"),
            CreatorRank = DbHelper.GetStringOrNull(r, "creator_rank"),
            RewardCoins = DbHelper.GetInt(r, "reward_coins"),
            PeriodStart = DbHelper.GetDateTimeOrNull(r, "period_start"),
            PeriodEnd = DbHelper.GetDateTimeOrNull(r, "period_end")
        }, parameters);
    }

    private static string GetCompetitionStatus(DateTime subStart, DateTime subEnd, DateTime? voteStart, DateTime? voteEnd)
    {
        var now = DateTime.UtcNow;
        if (now < subStart) return "upcoming";
        if (now >= subStart && now <= subEnd) return "submission_open";
        if (voteStart.HasValue && now >= voteStart && voteEnd.HasValue && now <= voteEnd) return "voting";
        return "ended";
    }

    public async Task<List<CompetitionResponse>> GetCompetitionsAsync(bool activeOnly, Guid? viewerId, int page, int pageSize)
    {
        // BUG#M6-4 FIX: NpgsqlConnection implements IAsyncDisposable; synchronous using
        // bypasses async teardown. Changed all 6 methods to await using.
        await using var conn = await _db.CreateConnectionAsync();
        pageSize = Math.Min(pageSize, 50);
        int offset = (page - 1) * pageSize;

        var where = activeOnly ? "WHERE c.is_active = TRUE" : "";

        var sql = $@"
            SELECT c.*
            FROM competitions c
            {where}
            ORDER BY c.is_featured DESC, c.submission_start DESC
            LIMIT @limit OFFSET @offset";

        var competitions = await DbHelper.ExecuteReaderAsync(conn, sql, r =>
        {
            var subStart = DbHelper.GetDateTime(r, "submission_start");
            var subEnd = DbHelper.GetDateTime(r, "submission_end");
            var voteStart = DbHelper.GetDateTimeOrNull(r, "voting_start");
            var voteEnd = DbHelper.GetDateTimeOrNull(r, "voting_end");

            return new CompetitionResponse
            {
                Id = DbHelper.GetGuid(r, "id"),
                Title = DbHelper.GetString(r, "title"),
                Description = DbHelper.GetStringOrNull(r, "description"),
                Rules = DbHelper.GetStringOrNull(r, "rules"),
                BannerUrl = DbHelper.GetStringOrNull(r, "banner_url"),
                Theme = DbHelper.GetStringOrNull(r, "theme"),
                PrizePoolCoins = DbHelper.GetLong(r, "prize_pool_coins"),
                SubmissionStart = subStart,
                SubmissionEnd = subEnd,
                VotingStart = voteStart,
                VotingEnd = voteEnd,
                ResultsAnnouncedAt = DbHelper.GetDateTimeOrNull(r, "results_announced_at"),
                MaxEntriesPerUser = DbHelper.GetInt(r, "max_entries_per_user"),
                MinWordCount = r.IsDBNull(r.GetOrdinal("min_word_count")) ? null : DbHelper.GetInt(r, "min_word_count"),
                MaxWordCount = r.IsDBNull(r.GetOrdinal("max_word_count")) ? null : DbHelper.GetInt(r, "max_word_count"),
                IsActive = DbHelper.GetBool(r, "is_active"),
                IsFeatured = DbHelper.GetBool(r, "is_featured"),
                TotalEntries = DbHelper.GetInt(r, "total_entries"),
                TotalVotes = DbHelper.GetInt(r, "total_votes"),
                Status = GetCompetitionStatus(subStart, subEnd, voteStart, voteEnd)
            };
        },
        new Dictionary<string, object?> { ["limit"] = pageSize, ["offset"] = offset });

        // If viewer, check if they've entered each competition
        if (viewerId.HasValue && competitions.Count > 0)
        {
            var compIds = competitions.Select(c => c.Id).ToList();
            var enteredIds = await DbHelper.ExecuteReaderAsync(conn,
                "SELECT DISTINCT competition_id FROM competition_entries WHERE user_id = @uid AND competition_id = ANY(@ids)",
                r => DbHelper.GetGuid(r, "competition_id"),
                new Dictionary<string, object?> { ["uid"] = viewerId.Value, ["ids"] = compIds.ToArray() });

            var enteredSet = enteredIds.ToHashSet();
            foreach (var comp in competitions)
                comp.HasEntered = enteredSet.Contains(comp.Id);
        }

        return competitions;
    }

    public async Task<CompetitionResponse?> GetCompetitionAsync(Guid competitionId, Guid? viewerId)
    {
        // BUG#M6-4 FIX: NpgsqlConnection implements IAsyncDisposable; synchronous using
        // bypasses async teardown. Changed all 6 methods to await using.
        await using var conn = await _db.CreateConnectionAsync();

        var comp = await DbHelper.ExecuteReaderFirstAsync(conn,
            "SELECT * FROM competitions WHERE id = @id",
            r =>
            {
                var subStart = DbHelper.GetDateTime(r, "submission_start");
                var subEnd = DbHelper.GetDateTime(r, "submission_end");
                var voteStart = DbHelper.GetDateTimeOrNull(r, "voting_start");
                var voteEnd = DbHelper.GetDateTimeOrNull(r, "voting_end");

                return new CompetitionResponse
                {
                    Id = DbHelper.GetGuid(r, "id"),
                    Title = DbHelper.GetString(r, "title"),
                    Description = DbHelper.GetStringOrNull(r, "description"),
                    Rules = DbHelper.GetStringOrNull(r, "rules"),
                    BannerUrl = DbHelper.GetStringOrNull(r, "banner_url"),
                    Theme = DbHelper.GetStringOrNull(r, "theme"),
                    PrizePoolCoins = DbHelper.GetLong(r, "prize_pool_coins"),
                    SubmissionStart = subStart,
                    SubmissionEnd = subEnd,
                    VotingStart = voteStart,
                    VotingEnd = voteEnd,
                    ResultsAnnouncedAt = DbHelper.GetDateTimeOrNull(r, "results_announced_at"),
                    MaxEntriesPerUser = DbHelper.GetInt(r, "max_entries_per_user"),
                    MinWordCount = r.IsDBNull(r.GetOrdinal("min_word_count")) ? null : DbHelper.GetInt(r, "min_word_count"),
                    MaxWordCount = r.IsDBNull(r.GetOrdinal("max_word_count")) ? null : DbHelper.GetInt(r, "max_word_count"),
                    IsActive = DbHelper.GetBool(r, "is_active"),
                    IsFeatured = DbHelper.GetBool(r, "is_featured"),
                    TotalEntries = DbHelper.GetInt(r, "total_entries"),
                    TotalVotes = DbHelper.GetInt(r, "total_votes"),
                    Status = GetCompetitionStatus(subStart, subEnd, voteStart, voteEnd)
                };
            },
            new Dictionary<string, object?> { ["id"] = competitionId });

        if (comp == null) return null;

        if (viewerId.HasValue)
        {
            comp.HasEntered = await DbHelper.ExecuteScalarAsync<bool>(conn,
                "SELECT EXISTS(SELECT 1 FROM competition_entries WHERE competition_id = @cid AND user_id = @uid)",
                new Dictionary<string, object?> { ["cid"] = competitionId, ["uid"] = viewerId.Value });
        }

        return comp;
    }

    public async Task<PagedResult<CompetitionEntryResponse>> GetCompetitionEntriesAsync(Guid competitionId, Guid? viewerId, int page, int pageSize)
    {
        // BUG#M6-4 FIX: NpgsqlConnection implements IAsyncDisposable; synchronous using
        // bypasses async teardown. Changed all 6 methods to await using.
        await using var conn = await _db.CreateConnectionAsync();
        pageSize = Math.Min(pageSize, 50);
        int offset = (page - 1) * pageSize;

        var total = await DbHelper.ExecuteScalarAsync<long>(conn,
            "SELECT COUNT(*) FROM competition_entries WHERE competition_id = @cid AND is_disqualified = FALSE",
            new Dictionary<string, object?> { ["cid"] = competitionId });

        var sql = @"
            SELECT ce.id, ce.competition_id, ce.user_id, ce.story_id, ce.submitted_at,
                   ce.vote_count, ce.final_rank, ce.is_winner,
                   u.username, u.avatar_url,
                   s.title as story_title, s.slug as story_slug, s.thumbnail_url as story_thumbnail_url,
                   CASE WHEN cv.id IS NOT NULL THEN TRUE ELSE FALSE END as has_voted
            FROM competition_entries ce
            JOIN users u ON u.id = ce.user_id
            JOIN stories s ON s.id = ce.story_id
            LEFT JOIN competition_votes cv ON cv.entry_id = ce.id AND cv.voter_id = @viewerId
            WHERE ce.competition_id = @cid AND ce.is_disqualified = FALSE
            ORDER BY ce.final_rank ASC NULLS LAST, ce.vote_count DESC
            LIMIT @limit OFFSET @offset";

        var items = await DbHelper.ExecuteReaderAsync(conn, sql, r => new CompetitionEntryResponse
        {
            Id = DbHelper.GetGuid(r, "id"),
            CompetitionId = DbHelper.GetGuid(r, "competition_id"),
            UserId = DbHelper.GetGuid(r, "user_id"),
            Username = DbHelper.GetString(r, "username"),
            AvatarUrl = DbHelper.GetStringOrNull(r, "avatar_url"),
            StoryId = DbHelper.GetGuid(r, "story_id"),
            StoryTitle = DbHelper.GetString(r, "story_title"),
            StorySlug = DbHelper.GetString(r, "story_slug"),
            StoryThumbnailUrl = DbHelper.GetStringOrNull(r, "story_thumbnail_url"),
            VoteCount = DbHelper.GetInt(r, "vote_count"),
            FinalRank = r.IsDBNull(r.GetOrdinal("final_rank")) ? null : DbHelper.GetInt(r, "final_rank"),
            IsWinner = DbHelper.GetBool(r, "is_winner"),
            SubmittedAt = DbHelper.GetDateTime(r, "submitted_at"),
            HasVoted = DbHelper.GetBool(r, "has_voted")
        },
        new Dictionary<string, object?> { ["cid"] = competitionId, ["viewerId"] = (object?)viewerId ?? DBNull.Value, ["limit"] = pageSize, ["offset"] = offset });

        return PagedResult<CompetitionEntryResponse>.Create(items, (int)total, page, pageSize);
    }

    public async Task<(bool Success, string Message)> SubmitEntryAsync(Guid userId, Guid competitionId, SubmitEntryRequest req)
    {
        // BUG#M6-4 FIX: NpgsqlConnection implements IAsyncDisposable; synchronous using
        // bypasses async teardown. Changed all 6 methods to await using.
        await using var conn = await _db.CreateConnectionAsync();

        var comp = await DbHelper.ExecuteReaderFirstAsync(conn,
            "SELECT id, submission_start, submission_end, max_entries_per_user FROM competitions WHERE id = @id AND is_active = TRUE",
            r => new
            {
                Id = DbHelper.GetGuid(r, "id"),
                SubStart = DbHelper.GetDateTime(r, "submission_start"),
                SubEnd = DbHelper.GetDateTime(r, "submission_end"),
                MaxEntries = DbHelper.GetInt(r, "max_entries_per_user")
            },
            new Dictionary<string, object?> { ["id"] = competitionId });

        if (comp == null) return (false, "Competition nahi mila");

        var now = DateTime.UtcNow;
        if (now < comp.SubStart) return (false, "Submission abhi shuru nahi hui");
        if (now > comp.SubEnd) return (false, "Submission window band ho gayi");

        // BUG#M6-6 FIX: alreadyEntered was a bool EXISTS check — always blocked the 2nd
        // entry even if max_entries_per_user > 1 (e.g. admin allows 3 entries per user).
        // Now compare actual entry COUNT against the competition's max_entries_per_user.
        var existingEntries = await DbHelper.ExecuteScalarAsync<int>(conn,
            "SELECT COUNT(*) FROM competition_entries WHERE competition_id = @cid AND user_id = @uid",
            new Dictionary<string, object?> { ["cid"] = competitionId, ["uid"] = userId });
        if (existingEntries >= comp.MaxEntries)
            return (false, $"Max {comp.MaxEntries} entr{(comp.MaxEntries == 1 ? "y" : "ies")} allowed");

        // Check story ownership
        var storyOwned = await DbHelper.ExecuteScalarAsync<bool>(conn,
            "SELECT EXISTS(SELECT 1 FROM stories WHERE id = @sid AND creator_id = @uid AND status = 'published')",
            new Dictionary<string, object?> { ["sid"] = req.StoryId, ["uid"] = userId });
        if (!storyOwned) return (false, "Story aapki nahi hai ya publish nahi hai");

        await DbHelper.ExecuteNonQueryAsync(conn,
            "INSERT INTO competition_entries (competition_id, user_id, story_id) VALUES (@cid, @uid, @sid)",
            new Dictionary<string, object?> { ["cid"] = competitionId, ["uid"] = userId, ["sid"] = req.StoryId });

        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE competitions SET total_entries = total_entries + 1 WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = competitionId });

        return (true, "Entry submit ho gayi! Best of luck! 👻");
    }

    public async Task<(bool Success, string Message)> VoteForEntryAsync(Guid userId, Guid competitionId, Guid entryId)
    {
        // BUG#M6-4 FIX: NpgsqlConnection implements IAsyncDisposable; synchronous using
        // bypasses async teardown. Changed all 6 methods to await using.
        await using var conn = await _db.CreateConnectionAsync();

        // Check competition is in voting phase
        var comp = await DbHelper.ExecuteReaderFirstAsync(conn,
            "SELECT voting_start, voting_end FROM competitions WHERE id = @id AND is_active = TRUE",
            r => new { VoteStart = DbHelper.GetDateTimeOrNull(r, "voting_start"), VoteEnd = DbHelper.GetDateTimeOrNull(r, "voting_end") },
            new Dictionary<string, object?> { ["id"] = competitionId });

        if (comp == null) return (false, "Competition nahi mila");
        if (!comp.VoteStart.HasValue || DateTime.UtcNow < comp.VoteStart.Value) return (false, "Voting abhi shuru nahi hui");
        if (!comp.VoteEnd.HasValue || DateTime.UtcNow > comp.VoteEnd.Value) return (false, "Voting band ho gayi");

        // Check entry belongs to competition
        var entry = await DbHelper.ExecuteReaderFirstAsync(conn,
            "SELECT id, user_id FROM competition_entries WHERE id = @eid AND competition_id = @cid AND is_disqualified = FALSE",
            r => new { Id = DbHelper.GetGuid(r, "id"), UserId = DbHelper.GetGuid(r, "user_id") },
            new Dictionary<string, object?> { ["eid"] = entryId, ["cid"] = competitionId });

        if (entry == null) return (false, "Entry nahi mili");
        if (entry.UserId == userId) return (false, "Apni entry ko vote nahi kar sakte");

        // Check already voted for this entry
        var alreadyVoted = await DbHelper.ExecuteScalarAsync<bool>(conn,
            "SELECT EXISTS(SELECT 1 FROM competition_votes WHERE entry_id = @eid AND voter_id = @uid)",
            new Dictionary<string, object?> { ["eid"] = entryId, ["uid"] = userId });
        if (alreadyVoted) return (false, "Pehle se vote de chuke hain");

        await DbHelper.ExecuteNonQueryAsync(conn,
            "INSERT INTO competition_votes (competition_id, entry_id, voter_id) VALUES (@cid, @eid, @uid)",
            new Dictionary<string, object?> { ["cid"] = competitionId, ["eid"] = entryId, ["uid"] = userId });

        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE competition_entries SET vote_count = vote_count + 1 WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = entryId });

        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE competitions SET total_votes = total_votes + 1 WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = competitionId });

        return (true, "Vote de diya! 🗳️");
    }
}
