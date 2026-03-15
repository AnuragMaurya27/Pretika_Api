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
                response.TotalUsers = await DbHelper.ExecuteScalarAsync<int>(conn,
                    @"SELECT COUNT(1) FROM users
                      WHERE deleted_at IS NULL
                        AND (LOWER(username) LIKE @q OR LOWER(display_name) LIKE @q OR LOWER(bio) LIKE @q)",
                    new() { ["@q"] = pattern });
            }

            response.Users = await DbHelper.ExecuteReaderAsync(conn,
                @"SELECT u.id, u.username, u.display_name, u.avatar_url, u.bio,
                         u.is_creator, u.is_verified_creator,
                         u.reader_fear_rank, u.creator_fear_rank,
                         u.total_followers, u.total_stories_published
                  FROM users u
                  WHERE u.deleted_at IS NULL
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

        // Check for duplicate report within 24h
        await using var conn = await _db.CreateConnectionAsync();
        var existing = await DbHelper.ExecuteScalarAsync<int>(conn,
            @"SELECT COUNT(1) FROM user_reports
              WHERE reporter_id=@rid AND entity_id=@eid AND entity_type=@etype
                AND created_at > NOW() - INTERVAL '24 hours'",
            new() { ["@rid"] = reporterId, ["@eid"] = req.EntityId, ["@etype"] = req.EntityType.ToLower() });

        if (existing > 0)
            return (false, "Aapne is content ko pehle hi report kar diya hai. 24 ghante mein dobara report nahi kar sakte.");

        var description = req.Description?.Trim();
        if (description?.Length > 500)
            description = description[..500];

        try
        {
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO user_reports (id, reporter_id, entity_type, entity_id, reason, description, status, created_at)
                  VALUES (@id, @rid, @etype, @eid, @reason, @desc, 'pending', NOW())",
                new()
                {
                    ["@id"]     = Guid.NewGuid(),
                    ["@rid"]    = reporterId,
                    ["@etype"]  = req.EntityType.ToLower(),
                    ["@eid"]    = req.EntityId,
                    ["@reason"] = req.Reason.ToLower(),
                    ["@desc"]   = (object?)description ?? DBNull.Value,
                });

            return (true, "Report submit ho gayi. Hamari team 24 ghante mein review karegi. Shukriya!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateReport failed");
            return (false, "Report submit nahi ho saki. Dobara try karo.");
        }
    }
}
