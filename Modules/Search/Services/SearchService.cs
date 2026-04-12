using HauntedVoiceUniverse.Infrastructure.Database;
using HauntedVoiceUniverse.Modules.Search.Models;
using HauntedVoiceUniverse.Modules.Stories.Models;
using HauntedVoiceUniverse.Modules.Stories.Services;
using Npgsql;

namespace HauntedVoiceUniverse.Modules.Search.Services;

public interface ISearchService
{
    Task<UnifiedSearchResponse> SearchAsync(UnifiedSearchRequest req, Guid? viewerId);
    Task<(bool Success, string Message)> CreateReportAsync(Guid reporterId, CreateReportRequest req);
}

public class SearchService : ISearchService
{
    private readonly IDbConnectionFactory _db;
    private readonly IStoryService _storyService;
    private readonly ILogger<SearchService> _logger;

    public SearchService(IDbConnectionFactory db, IStoryService storyService, ILogger<SearchService> logger)
    {
        _db = db;
        _storyService = storyService;
        _logger = logger;
    }

    // ─── UNIFIED SEARCH ───────────────────────────────────────────────────────
    public async Task<UnifiedSearchResponse> SearchAsync(UnifiedSearchRequest req, Guid? viewerId)
    {
        var response = new UnifiedSearchResponse
        {
            Query = req.Q.Trim(),
            SearchType = req.SearchType.ToLower()
        };

        var searchType = req.SearchType.ToLower();
        var pageSize = Math.Min(req.PageSize, 30);
        var page = Math.Max(req.Page, 1);

        // ── Stories ──────────────────────────────────────────────────────────
        if (searchType == "all" || searchType == "stories")
        {
            var filter = new StoryFilterRequest
            {
                Search = req.Q.Trim(),
                SortBy = req.SortBy ?? "latest",
                Language = req.Language,
                Category = req.Category,
                StoryType = req.StoryType,
                AgeRating = req.AgeRating,
                DateFrom = req.DateFrom,
                DateTo = req.DateTo,
                Page = page,
                PageSize = pageSize,
            };

            var storyResult = await _storyService.GetStoriesAsync(filter, viewerId);

            response.Stories = new SearchStoriesPage
            {
                Items = storyResult.Items,
                TotalCount = storyResult.TotalCount,
                Page = storyResult.Page,
                PageSize = storyResult.PageSize,
            };
        }

        // ── Users / Creators ─────────────────────────────────────────────────
        if (searchType == "all" || searchType == "users")
        {
            var userLimit = searchType == "all" ? 6 : 20; // fewer in combined mode
            var userOffset = searchType == "all" ? 0 : (page - 1) * pageSize;

            await using var conn = await _db.CreateConnectionAsync();
            var pattern = $"%{req.Q.Trim().ToLower()}%";

            // Count total for users-only mode
            if (searchType == "users")
            {
                // BUG#M9-4 FIX: count must also exclude shadow-banned users.
                response.TotalUsers = await DbHelper.ExecuteScalarAsync<int>(conn,
                    @"SELECT COUNT(1) FROM users
                      WHERE deleted_at IS NULL
                        AND status NOT IN ('shadow_banned','banned','suspended')
                        AND (LOWER(username) LIKE @q OR LOWER(display_name) LIKE @q OR LOWER(bio) LIKE @q)",
                    new() { ["@q"] = pattern });
            }

            // BUG#M9-4 FIX: Exclude shadow-banned (and banned/suspended/deleted) users from search.
            response.Users = await DbHelper.ExecuteReaderAsync(conn,
                @"SELECT u.id, u.username, u.display_name, u.avatar_url, u.bio,
                         u.is_creator, u.is_verified_creator,
                         u.reader_fear_rank, u.creator_fear_rank,
                         u.total_followers, u.total_stories_published
                  FROM users u
                  WHERE u.deleted_at IS NULL
                    AND u.status NOT IN ('shadow_banned','banned','suspended')
                    AND (LOWER(u.username) LIKE @q OR LOWER(u.display_name) LIKE @q OR LOWER(u.bio) LIKE @q)
                  ORDER BY u.is_verified_creator DESC, u.total_followers DESC NULLS LAST
                  LIMIT @lim OFFSET @off",
                r => new SearchUserResult
                {
                    Id              = DbHelper.GetGuid(r, "id"),
                    Username        = DbHelper.GetString(r, "username"),
                    DisplayName     = DbHelper.GetStringOrNull(r, "display_name"),
                    AvatarUrl       = DbHelper.GetStringOrNull(r, "avatar_url"),
                    Bio             = DbHelper.GetStringOrNull(r, "bio"),
                    IsCreator       = DbHelper.GetBool(r, "is_creator"),
                    IsVerifiedCreator = DbHelper.GetBool(r, "is_verified_creator"),
                    ReaderFearRank  = DbHelper.GetString(r, "reader_fear_rank"),
                    CreatorFearRank = DbHelper.GetStringOrNull(r, "creator_fear_rank"),
                    TotalFollowers  = DbHelper.GetInt(r, "total_followers"),
                    TotalStories    = DbHelper.GetInt(r, "total_stories_published"),
                },
                new()
                {
                    ["@q"]   = pattern,
                    ["@lim"] = userLimit,
                    ["@off"] = userOffset,
                });

            if (searchType == "all")
                response.TotalUsers = response.Users.Count;
        }

        return response;
    }

    // ─── CREATE REPORT ────────────────────────────────────────────────────────
    public async Task<(bool Success, string Message)> CreateReportAsync(Guid reporterId, CreateReportRequest req)
    {
        // Validate entity type
        var validTypes = new[] { "story", "episode", "user", "comment" };
        if (!validTypes.Contains(req.EntityType.ToLower()))
            return (false, "Invalid entity type. Use: story, episode, user, comment");

        // Validate reason
        var validReasons = new[] { "inappropriate", "spam", "hate_speech", "adult_content", "copyright", "misinformation", "other" };
        if (!validReasons.Contains(req.Reason.ToLower()))
            return (false, "Invalid reason.");

        // Don't allow self-report (for user type)
        if (req.EntityType.ToLower() == "user" && req.EntityId == reporterId)
            return (false, "Aap apne aap ko report nahi kar sakte.");

        await using var conn = await _db.CreateConnectionAsync();

        // BUG#M9-10 FIX: Per-user rate limit — max 20 reports per hour to prevent abuse.
        var recentReports = await DbHelper.ExecuteScalarAsync<int>(conn,
            @"SELECT COUNT(1) FROM reports
              WHERE reporter_id = @rid AND created_at > NOW() - INTERVAL '1 hour'",
            new() { ["@rid"] = reporterId });
        if (recentReports >= 20)
            return (false, "Aap ek ghante mein zyada reports submit nahi kar sakte. Baad mein try karo.");

        // BUG#M9-1 FIX: All reports go to the unified 'reports' table (previously story/user/episode
        // reports went to 'user_reports' which the admin panel never reads — making them invisible).
        // Check for duplicate report within 24h
        var existing = await DbHelper.ExecuteScalarAsync<int>(conn,
            @"SELECT COUNT(1) FROM reports
              WHERE reporter_id=@rid AND entity_id=@eid AND entity_type=@etype
                AND created_at > NOW() - INTERVAL '24 hours'",
            new() { ["@rid"] = reporterId, ["@eid"] = req.EntityId, ["@etype"] = req.EntityType.ToLower() });

        if (existing > 0)
            return (false, "Aapne is content ko pehle hi report kar diya hai. 24 ghante mein dobara report nahi kar sakte.");

        var description = req.Description?.Trim();
        if (description?.Length > 500)
            description = description[..500];

        // BUG#M9-8 FIX (partial): Assign severity based on reason so the admin queue can sort
        // high-severity reports to the top. hate_speech/adult_content = critical; inappropriate = high;
        // others = medium. The queue ORDER BY severity DESC will surface urgent reports first.
        var severity = req.Reason.ToLower() switch
        {
            "hate_speech"     => "critical",
            "adult_content"   => "critical",
            "inappropriate"   => "high",
            "misinformation"  => "high",
            "spam"            => "medium",
            "copyright"       => "medium",
            _                 => "low",
        };

        try
        {
            // BUG#M9-1 FIX: Insert into 'reports' table (same table admin panel reads).
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO reports (reporter_id, entity_type, entity_id, reason, custom_reason, severity, status, created_at)
                  VALUES (@rid, @etype, @eid, @reason, @custom, @sev::report_severity, 'pending', NOW())",
                new()
                {
                    ["@rid"]    = reporterId,
                    ["@etype"]  = req.EntityType.ToLower(),
                    ["@eid"]    = req.EntityId,
                    ["@reason"] = req.Reason.ToLower(),
                    ["@custom"] = (object?)description ?? DBNull.Value,
                    ["@sev"]    = severity,
                });

            // BUG#M9-2 FIX: Auto-hide content when ≥10 unique users report the same entity
            // within 24h — prevents coordinated harassment campaigns from harming creators
            // while awaiting manual review.
            var reportCount = await DbHelper.ExecuteScalarAsync<int>(conn,
                @"SELECT COUNT(DISTINCT reporter_id) FROM reports
                  WHERE entity_id=@eid AND entity_type=@etype AND status='pending'
                    AND created_at > NOW() - INTERVAL '24 hours'",
                new() { ["@eid"] = req.EntityId, ["@etype"] = req.EntityType.ToLower() });

            if (reportCount >= 10)
            {
                // Auto-hide content pending review — does NOT permanently ban/remove.
                // Story/episode: set status to 'under_review'; comment: set is_hidden=TRUE; user: flag only.
                switch (req.EntityType.ToLower())
                {
                    case "story":
                    case "episode":
                        await DbHelper.ExecuteNonQueryAsync(conn,
                            "UPDATE stories SET status='under_review'::story_status WHERE id=@id AND status='published'",
                            new() { ["@id"] = req.EntityId });
                        break;
                    case "comment":
                        await DbHelper.ExecuteNonQueryAsync(conn,
                            "UPDATE comments SET is_hidden=TRUE WHERE id=@id",
                            new() { ["@id"] = req.EntityId });
                        break;
                    // 'user' type: only flag, don't auto-ban — human review required
                }
            }

            return (true, "Report submit ho gayi. Hamari team 24 ghante mein review karegi. Shukriya!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateReport failed");
            return (false, "Report submit nahi ho saki. Dobara try karo.");
        }
    }
}
