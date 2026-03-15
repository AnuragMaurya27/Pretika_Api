using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Infrastructure.Database;
using HauntedVoiceUniverse.Modules.Stories.Models;
using Npgsql;
using System.Text.RegularExpressions;

namespace HauntedVoiceUniverse.Modules.Stories.Services;

public interface IStoryService
{
    Task<(bool Success, string Message, StoryResponse? Data)> CreateStoryAsync(Guid creatorId, CreateStoryRequest req);
    Task<(bool Success, string Message, StoryResponse? Data)> UpdateStoryAsync(Guid creatorId, Guid storyId, UpdateStoryRequest req);
    Task<(bool Success, string Message)> DeleteStoryAsync(Guid creatorId, Guid storyId);
    Task<(bool Success, string Message)> PublishStoryAsync(Guid creatorId, Guid storyId);
    Task<(bool Success, string Message)> UnpublishStoryAsync(Guid creatorId, Guid storyId);
    Task<StoryDetailResponse?> GetStoryBySlugAsync(string slug, Guid? viewerId);
    Task<StoryDetailResponse?> GetStoryByIdAsync(Guid storyId, Guid? viewerId);
    Task<PagedResult<StoryResponse>> GetStoriesAsync(StoryFilterRequest filter, Guid? viewerId);
    Task<PagedResult<StoryResponse>> GetMyStoriesAsync(Guid creatorId, string? status, int page, int pageSize);
    Task<PagedResult<StoryResponse>> GetCreatorStoriesAsync(Guid creatorId, Guid? viewerId, int page, int pageSize);

    // Episodes
    Task<(bool Success, string Message, EpisodeResponse? Data)> CreateEpisodeAsync(Guid creatorId, Guid storyId, CreateEpisodeRequest req);
    Task<(bool Success, string Message, EpisodeResponse? Data)> UpdateEpisodeAsync(Guid creatorId, Guid storyId, Guid episodeId, UpdateEpisodeRequest req);
    Task<(bool Success, string Message)> DeleteEpisodeAsync(Guid creatorId, Guid storyId, Guid episodeId);
    Task<(bool Success, string Message)> PublishEpisodeAsync(Guid creatorId, Guid storyId, Guid episodeId);
    Task<List<EpisodeResponse>> GetEpisodesAsync(Guid storyId, Guid? viewerId);
    Task<EpisodeResponse?> GetEpisodeAsync(Guid storyId, Guid episodeId, Guid? viewerId);

    // Interactions
    Task<(bool Success, string Message)> LikeStoryAsync(Guid userId, Guid storyId);
    Task<(bool Success, string Message)> UnlikeStoryAsync(Guid userId, Guid storyId);
    Task<(bool Success, string Message)> BookmarkStoryAsync(Guid userId, Guid storyId);
    Task<(bool Success, string Message)> UnbookmarkStoryAsync(Guid userId, Guid storyId);
    Task<PagedResult<StoryResponse>> GetBookmarkedStoriesAsync(Guid userId, int page, int pageSize);
    Task RecordViewAsync(Guid storyId, Guid? episodeId, Guid? userId, string ipAddress, string? userAgent);

    // Unlock
    Task<(bool Success, string Message)> UnlockEpisodeAsync(Guid userId, Guid episodeId);

    // Categories
    Task<List<CategoryResponse>> GetCategoriesAsync();
    Task<(bool Success, string Message, CategoryResponse? Data)> CreateCategoryAsync(string name, string? description, string? iconUrl);
    Task<(bool Success, string Message)> DeleteCategoryAsync(Guid categoryId);

    // Rating
    Task<(bool Success, string Message, object? Data)> RateStoryAsync(Guid userId, Guid storyId, int rating);
}

public class StoryService : IStoryService
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<StoryService> _logger;

    public StoryService(IDbConnectionFactory db, ILogger<StoryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ─── CREATE STORY ─────────────────────────────────────────────────────────
    public async Task<(bool Success, string Message, StoryResponse? Data)> CreateStoryAsync(
        Guid creatorId, CreateStoryRequest req)
    {
        // Creator check
        await using var conn = await _db.CreateConnectionAsync();
        var isCreator = await DbHelper.ExecuteScalarAsync<bool>(conn,
            "SELECT is_creator FROM users WHERE id=@id AND deleted_at IS NULL",
            new() { ["@id"] = creatorId });
        if (!isCreator)
            return (false, "Pehle creator ban jao. Profile mein creator apply karo.", null);

        var storyId = Guid.NewGuid();
        var slug = await GenerateUniqueSlugAsync(conn, req.Title);

        await using var tx = await conn.BeginTransactionAsync();
        bool committed = false;
        try
        {
            await DbHelper.ExecuteNonQueryAsync(conn, @"
                INSERT INTO stories (id, creator_id, category_id, title, slug, summary,
                    story_type, language, age_rating, status, thumbnail_url, created_at, updated_at)
                VALUES (@id, @cid, @catId, @title, @slug, @summary,
                    @type::story_type, @lang::content_language, @age::age_rating,
                    'draft'::story_status, @thumb, NOW(), NOW())",
                new()
                {
                    ["@id"]      = storyId,
                    ["@cid"]     = creatorId,
                    ["@catId"]   = (object?)req.CategoryId ?? DBNull.Value,
                    ["@title"]   = req.Title.Trim(),
                    ["@slug"]    = slug,
                    ["@summary"] = (object?)req.Summary ?? DBNull.Value,
                    ["@type"]    = req.StoryType.ToLower(),
                    ["@lang"]    = req.Language.ToLower(),
                    ["@age"]     = req.AgeRating.ToLower(),
                    ["@thumb"]   = (object?)req.ThumbnailUrl ?? DBNull.Value,
                }, tx);

            // Tags
            if (req.Tags.Any())
                await UpsertTagsAsync(conn, storyId, req.Tags, tx);

            await tx.CommitAsync();
            committed = true;
        }
        catch (Exception ex)
        {
            if (!committed) try { await tx.RollbackAsync(); } catch { }
            _logger.LogError(ex, "CreateStory failed: {Msg}", ex.Message);
            return (false, $"Story create failed: {ex.Message}", null);
        }

        var story = await GetStoryByIdAsync(storyId, creatorId);
        return (true, "Story create ho gayi! Ab episodes add karo.", story);
    }

    // ─── UPDATE STORY ─────────────────────────────────────────────────────────
    public async Task<(bool Success, string Message, StoryResponse? Data)> UpdateStoryAsync(
        Guid creatorId, Guid storyId, UpdateStoryRequest req)
    {
        await using var conn = await _db.CreateConnectionAsync();

        var exists = await DbHelper.ExecuteScalarAsync<int>(conn,
            "SELECT COUNT(1) FROM stories WHERE id=@id AND creator_id=@cid AND deleted_at IS NULL",
            new() { ["@id"] = storyId, ["@cid"] = creatorId });
        if (exists == 0) return (false, "Story nahi mili ya permission nahi hai", null);

        var updates = new List<string>();
        var p = new Dictionary<string, object?> { ["@id"] = storyId };

        if (req.Title != null) { updates.Add("title=@title"); p["@title"] = req.Title.Trim(); }
        if (req.Summary != null) { updates.Add("summary=@summary"); p["@summary"] = req.Summary; }
        if (req.CategoryId != null) { updates.Add("category_id=@catId"); p["@catId"] = req.CategoryId; }
        if (req.Language != null) { updates.Add("language=@lang::content_language"); p["@lang"] = req.Language.ToLower(); }
        if (req.AgeRating != null) { updates.Add("age_rating=@age::age_rating"); p["@age"] = req.AgeRating.ToLower(); }
        if (req.ThumbnailUrl != null) { updates.Add("thumbnail_url=@thumb"); p["@thumb"] = req.ThumbnailUrl.Trim() == "" ? (object)DBNull.Value : req.ThumbnailUrl.Trim(); }

        if (updates.Count > 0)
        {
            updates.Add("updated_at=NOW()");
            await DbHelper.ExecuteNonQueryAsync(conn,
                $"UPDATE stories SET {string.Join(",", updates)} WHERE id=@id", p);
        }

        if (req.Tags != null)
        {
            await DbHelper.ExecuteNonQueryAsync(conn,
                "DELETE FROM story_tags WHERE story_id=@id", new() { ["@id"] = storyId });
            if (req.Tags.Any())
                await UpsertTagsAsync(conn, storyId, req.Tags, null);
        }

        var story = await GetStoryByIdAsync(storyId, creatorId);
        return (true, "Story update ho gayi!", story);
    }

    // ─── DELETE STORY ─────────────────────────────────────────────────────────
    public async Task<(bool Success, string Message)> DeleteStoryAsync(Guid creatorId, Guid storyId)
    {
        await using var conn = await _db.CreateConnectionAsync();
        var rows = await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE stories SET deleted_at=NOW(), status='archived'::story_status WHERE id=@id AND creator_id=@cid AND deleted_at IS NULL",
            new() { ["@id"] = storyId, ["@cid"] = creatorId });
        return rows > 0 ? (true, "Story delete ho gayi!") : (false, "Story nahi mili");
    }

    // ─── PUBLISH STORY ────────────────────────────────────────────────────────
    public async Task<(bool Success, string Message)> PublishStoryAsync(Guid creatorId, Guid storyId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        var episodeCount = await DbHelper.ExecuteScalarAsync<int>(conn,
            "SELECT COUNT(1) FROM episodes WHERE story_id=@id AND deleted_at IS NULL",
            new() { ["@id"] = storyId });
        if (episodeCount == 0)
            return (false, "Kam se kam ek episode add karo pehle");

        var rows = await DbHelper.ExecuteNonQueryAsync(conn, @"
            UPDATE stories SET status='published'::story_status,
                published_at=COALESCE(published_at, NOW()), updated_at=NOW()
            WHERE id=@id AND creator_id=@cid AND deleted_at IS NULL",
            new() { ["@id"] = storyId, ["@cid"] = creatorId });

        if (rows == 0) return (false, "Story nahi mili ya permission nahi");

        // Update creator stats
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE users SET total_stories_published=total_stories_published+1 WHERE id=@id",
            new() { ["@id"] = creatorId });

        return (true, "Story publish ho gayi! 🎉");
    }

    // ─── UNPUBLISH STORY ──────────────────────────────────────────────────────
    public async Task<(bool Success, string Message)> UnpublishStoryAsync(Guid creatorId, Guid storyId)
    {
        await using var conn = await _db.CreateConnectionAsync();
        var rows = await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE stories SET status='draft'::story_status, updated_at=NOW() WHERE id=@id AND creator_id=@cid",
            new() { ["@id"] = storyId, ["@cid"] = creatorId });
        return rows > 0 ? (true, "Story unpublish ho gayi") : (false, "Story nahi mili");
    }

    // ─── GET STORY BY SLUG ────────────────────────────────────────────────────
    public async Task<StoryDetailResponse?> GetStoryBySlugAsync(string slug, Guid? viewerId)
    {
        await using var conn = await _db.CreateConnectionAsync();
        var storyId = await DbHelper.ExecuteScalarAsync<Guid?>(conn,
            "SELECT id FROM stories WHERE slug=@slug AND deleted_at IS NULL",
            new() { ["@slug"] = slug });
        if (!storyId.HasValue) return null;
        return await GetStoryByIdAsync(storyId.Value, viewerId);
    }

    // ─── GET STORY BY ID ──────────────────────────────────────────────────────
    public async Task<StoryDetailResponse?> GetStoryByIdAsync(Guid storyId, Guid? viewerId)
    {
        StoryDetailResponse? story = null;

        await using (var conn = await _db.CreateConnectionAsync())
        {
            using var cmd = new NpgsqlCommand(@"
                SELECT s.id, s.title, s.slug, s.summary, s.thumbnail_url, s.story_type,
                       s.language, s.age_rating, s.status, s.creator_id,
                       s.category_id, s.total_episodes, s.total_views, s.total_likes,
                       s.total_comments, s.total_bookmarks, s.engagement_score,
                       s.is_editor_pick, s.published_at, s.created_at, s.updated_at,
                       u.username as creator_username, u.display_name as creator_display_name,
                       u.avatar_url as creator_avatar_url, u.is_verified_creator,
                       c.name as category_name
                FROM stories s
                JOIN users u ON u.id = s.creator_id
                LEFT JOIN categories c ON c.id = s.category_id
                WHERE s.id = @id AND s.deleted_at IS NULL", conn);
            cmd.Parameters.AddWithValue("@id", storyId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                story = MapToStoryDetail(reader);
        }

        if (story == null) return null;

        // Tags
        await using (var conn2 = await _db.CreateConnectionAsync())
        {
            using var tagCmd = new NpgsqlCommand(@"
                SELECT t.name FROM tags t
                JOIN story_tags st ON st.tag_id = t.id
                WHERE st.story_id = @id", conn2);
            tagCmd.Parameters.AddWithValue("@id", storyId);
            using var tagReader = await tagCmd.ExecuteReaderAsync();
            while (await tagReader.ReadAsync())
                story.Tags.Add(tagReader.GetString(0));
        }

        // Episodes
        await using (var conn3 = await _db.CreateConnectionAsync())
        {
            using var epCmd = new NpgsqlCommand(@"
                SELECT id, story_id, title, slug, episode_number, access_type,
                       unlock_coin_cost, status, word_count, estimated_read_time_seconds,
                       total_views, total_likes, total_comments, published_at, created_at
                FROM episodes
                WHERE story_id=@id AND deleted_at IS NULL
                ORDER BY episode_number ASC", conn3);
            epCmd.Parameters.AddWithValue("@id", storyId);
            using var epReader = await epCmd.ExecuteReaderAsync();
            while (await epReader.ReadAsync())
                story.Episodes.Add(MapToEpisode(epReader, includeContent: false));
        }

        // Rating stats (for everyone)
        await using (var connR = await _db.CreateConnectionAsync())
        {
            using var rCmd = new NpgsqlCommand(
                "SELECT COALESCE(ROUND(AVG(rating)::numeric,1),0) as avg_r, COUNT(1) as cnt FROM story_ratings WHERE story_id=@sid",
                connR);
            rCmd.Parameters.AddWithValue("@sid", storyId);
            using var rr = await rCmd.ExecuteReaderAsync();
            if (await rr.ReadAsync())
            {
                story.AverageRating = (double)rr.GetDecimal(0);
                story.RatingCount = (int)rr.GetInt64(1);
            }
        }

        // Viewer context
        if (viewerId.HasValue)
        {
            await using var conn4 = await _db.CreateConnectionAsync();
            var isLiked = await DbHelper.ExecuteScalarAsync<int>(conn4,
                "SELECT COUNT(1) FROM story_likes WHERE user_id=@uid AND story_id=@sid",
                new() { ["@uid"] = viewerId, ["@sid"] = storyId });
            var isBookmarked = await DbHelper.ExecuteScalarAsync<int>(conn4,
                "SELECT COUNT(1) FROM bookmarks WHERE user_id=@uid AND story_id=@sid",
                new() { ["@uid"] = viewerId, ["@sid"] = storyId });
            story.IsLiked = isLiked > 0;
            story.IsBookmarked = isBookmarked > 0;

            // My rating
            var myRating = await DbHelper.ExecuteScalarAsync<int?>(conn4,
                "SELECT rating FROM story_ratings WHERE user_id=@uid AND story_id=@sid LIMIT 1",
                new() { ["@uid"] = viewerId, ["@sid"] = storyId });
            story.MyRating = myRating;

            // Mark which episodes are unlocked
            foreach (var ep in story.Episodes.Where(e => e.AccessType == "premium"))
            {
                var unlocked = await DbHelper.ExecuteScalarAsync<int>(conn4,
                    "SELECT COUNT(1) FROM episode_unlocks WHERE user_id=@uid AND episode_id=@eid",
                    new() { ["@uid"] = viewerId, ["@eid"] = ep.Id });
                ep.IsUnlocked = unlocked > 0;
            }
        }

        // Free episodes - content dikhao
        foreach (var ep in story.Episodes.Where(e => e.AccessType == "free"))
            ep.IsUnlocked = true;

        return story;
    }

    // ─── GET STORIES (Feed) ───────────────────────────────────────────────────
    public async Task<PagedResult<StoryResponse>> GetStoriesAsync(StoryFilterRequest filter, Guid? viewerId)
    {
        var conditions = new List<string> { "s.status='published'::story_status", "s.deleted_at IS NULL" };
        var p = new Dictionary<string, object?>();

        if (!string.IsNullOrEmpty(filter.Category))
        {
            conditions.Add("c.slug=@cat");
            p["@cat"] = filter.Category;
        }
        if (!string.IsNullOrEmpty(filter.Language))
        {
            conditions.Add("s.language=@lang::content_language");
            p["@lang"] = filter.Language.ToLower();
        }
        if (!string.IsNullOrEmpty(filter.StoryType))
        {
            conditions.Add("s.story_type=@type::story_type");
            p["@type"] = filter.StoryType.ToLower();
        }
        if (!string.IsNullOrEmpty(filter.AgeRating))
        {
            conditions.Add("s.age_rating=@age::age_rating");
            p["@age"] = filter.AgeRating.ToLower();
        }
        if (!string.IsNullOrEmpty(filter.Search))
        {
            // Search across: title, summary, creator username/display_name, category name, tags
            conditions.Add(@"(
                s.title ILIKE @search
                OR s.summary ILIKE @search
                OR u.username ILIKE @search
                OR u.display_name ILIKE @search
                OR c.name ILIKE @search
                OR c.slug ILIKE @search
                OR EXISTS (
                    SELECT 1 FROM story_tags st
                    JOIN tags tg ON tg.id = st.tag_id
                    WHERE st.story_id = s.id AND tg.name ILIKE @search
                )
            )");
            p["@search"] = $"%{filter.Search}%";
        }

        if (!string.IsNullOrEmpty(filter.CreatorUsername))
        {
            conditions.Add("(u.username ILIKE @creatorUser OR u.display_name ILIKE @creatorUser)");
            p["@creatorUser"] = $"%{filter.CreatorUsername}%";
        }

        if (!string.IsNullOrEmpty(filter.DateFrom) && DateTime.TryParse(filter.DateFrom, out var dateFrom))
        {
            conditions.Add("s.published_at >= @dateFrom");
            p["@dateFrom"] = dateFrom;
        }
        if (!string.IsNullOrEmpty(filter.DateTo) && DateTime.TryParse(filter.DateTo, out var dateTo))
        {
            conditions.Add("s.published_at <= @dateTo");
            p["@dateTo"] = dateTo.AddDays(1); // inclusive of end date
        }

        var where = string.Join(" AND ", conditions);
        var orderBy = filter.SortBy switch
        {
            "trending"          => "s.trending_score DESC, s.published_at DESC",
            "most_viewed"       => "s.total_views DESC, s.published_at DESC",
            "most_liked"        => "s.total_likes DESC, s.published_at DESC",
            "top_rated"         => "(SELECT COALESCE(AVG(rating),0) FROM story_ratings WHERE story_id=s.id) DESC, s.published_at DESC",
            "recently_updated"  => "s.updated_at DESC",
            _                   => "s.published_at DESC"
        };

        var pageSize = Math.Min(filter.PageSize, 50);
        var offset = (filter.Page - 1) * pageSize;

        var results = new List<StoryResponse>();
        int totalCount = 0;

        await using (var conn = await _db.CreateConnectionAsync())
        {
            totalCount = await DbHelper.ExecuteScalarAsync<int>(conn,
                $"SELECT COUNT(1) FROM stories s JOIN users u ON u.id=s.creator_id LEFT JOIN categories c ON c.id=s.category_id WHERE {where}", p);

            p["@limit"] = pageSize;
            p["@offset"] = offset;

            using var cmd = new NpgsqlCommand($@"
                SELECT s.id, s.title, s.slug, s.summary, s.thumbnail_url, s.story_type,
                       s.language, s.age_rating, s.status, s.creator_id, s.category_id,
                       s.total_episodes, s.total_views, s.total_likes, s.total_comments,
                       s.total_bookmarks, s.engagement_score, s.is_editor_pick,
                       s.published_at, s.created_at, s.updated_at,
                       u.username as creator_username, u.display_name as creator_display_name,
                       u.avatar_url as creator_avatar_url, u.is_verified_creator,
                       c.name as category_name
                FROM stories s
                JOIN users u ON u.id=s.creator_id
                LEFT JOIN categories c ON c.id=s.category_id
                WHERE {where}
                ORDER BY {orderBy}
                LIMIT @limit OFFSET @offset", conn);

            foreach (var param in p)
                cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                results.Add(MapToStory(reader));
        }

        // Viewer context - liked/bookmarked
        if (viewerId.HasValue && results.Count > 0)
        {
            await using var conn2 = await _db.CreateConnectionAsync();
            var ids = results.Select(r => r.Id).ToList();
            foreach (var story in results)
            {
                var liked = await DbHelper.ExecuteScalarAsync<int>(conn2,
                    "SELECT COUNT(1) FROM story_likes WHERE user_id=@uid AND story_id=@sid",
                    new() { ["@uid"] = viewerId, ["@sid"] = story.Id });
                story.IsLiked = liked > 0;
            }
        }

        return new PagedResult<StoryResponse>
        {
            Items = results, TotalCount = totalCount,
            Page = filter.Page, PageSize = pageSize
        };
    }

    // ─── GET MY STORIES ───────────────────────────────────────────────────────
    public async Task<PagedResult<StoryResponse>> GetMyStoriesAsync(
        Guid creatorId, string? status, int page, int pageSize)
    {
        pageSize = Math.Min(pageSize, 50);
        var offset = (page - 1) * pageSize;
        var results = new List<StoryResponse>();
        int totalCount = 0;

        // Treat empty string same as null — avoid invalid PostgreSQL enum cast
        var effectiveStatus = string.IsNullOrWhiteSpace(status) ? null : status.ToLower();
        var statusCondition = effectiveStatus != null ? "AND s.status=@status::story_status" : "";
        var p = new Dictionary<string, object?> { ["@cid"] = creatorId, ["@limit"] = pageSize, ["@offset"] = offset };
        if (effectiveStatus != null) p["@status"] = effectiveStatus;

        await using var conn = await _db.CreateConnectionAsync();
        var countParams = new Dictionary<string, object?> { ["@cid"] = creatorId };
        if (effectiveStatus != null) countParams["@status"] = effectiveStatus;
        totalCount = await DbHelper.ExecuteScalarAsync<int>(conn,
            $"SELECT COUNT(1) FROM stories s WHERE s.creator_id=@cid AND s.deleted_at IS NULL {statusCondition}",
            countParams);

        using var cmd = new NpgsqlCommand($@"
            SELECT s.id, s.title, s.slug, s.summary, s.thumbnail_url, s.story_type,
                   s.language, s.age_rating, s.status, s.creator_id, s.category_id,
                   s.total_episodes, s.total_views, s.total_likes, s.total_comments,
                   s.total_bookmarks, s.engagement_score, s.is_editor_pick,
                   s.published_at, s.created_at, s.updated_at,
                   u.username as creator_username, u.display_name as creator_display_name,
                   u.avatar_url as creator_avatar_url, u.is_verified_creator,
                   c.name as category_name
            FROM stories s
            JOIN users u ON u.id=s.creator_id
            LEFT JOIN categories c ON c.id=s.category_id
            WHERE s.creator_id=@cid AND s.deleted_at IS NULL {statusCondition}
            ORDER BY s.updated_at DESC
            LIMIT @limit OFFSET @offset", conn);

        foreach (var param in p)
            cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(MapToStory(reader));

        return new PagedResult<StoryResponse>
        {
            Items = results, TotalCount = totalCount, Page = page, PageSize = pageSize
        };
    }

    // ─── GET CREATOR STORIES ──────────────────────────────────────────────────
    public async Task<PagedResult<StoryResponse>> GetCreatorStoriesAsync(
        Guid creatorId, Guid? viewerId, int page, int pageSize)
    {
        pageSize = Math.Min(pageSize, 50);
        var offset = (page - 1) * pageSize;
        var results = new List<StoryResponse>();
        int totalCount = 0;

        await using (var conn = await _db.CreateConnectionAsync())
        {
            totalCount = await DbHelper.ExecuteScalarAsync<int>(conn,
                "SELECT COUNT(1) FROM stories WHERE creator_id=@cid AND status='published'::story_status AND deleted_at IS NULL",
                new() { ["@cid"] = creatorId });

            using var cmd = new NpgsqlCommand(@"
                SELECT s.id, s.title, s.slug, s.summary, s.thumbnail_url, s.story_type,
                       s.language, s.age_rating, s.status, s.creator_id, s.category_id,
                       s.total_episodes, s.total_views, s.total_likes, s.total_comments,
                       s.total_bookmarks, s.engagement_score, s.is_editor_pick,
                       s.published_at, s.created_at, s.updated_at,
                       u.username as creator_username, u.display_name as creator_display_name,
                       u.avatar_url as creator_avatar_url, u.is_verified_creator,
                       c.name as category_name
                FROM stories s JOIN users u ON u.id=s.creator_id
                LEFT JOIN categories c ON c.id=s.category_id
                WHERE s.creator_id=@cid AND s.status='published'::story_status AND s.deleted_at IS NULL
                ORDER BY s.published_at DESC LIMIT @lim OFFSET @off", conn);
            cmd.Parameters.AddWithValue("@cid", creatorId);
            cmd.Parameters.AddWithValue("@lim", pageSize);
            cmd.Parameters.AddWithValue("@off", offset);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                results.Add(MapToStory(reader));
        }

        return new PagedResult<StoryResponse>
        {
            Items = results, TotalCount = totalCount, Page = page, PageSize = pageSize
        };
    }

    // ─── CREATE EPISODE ───────────────────────────────────────────────────────
    public async Task<(bool Success, string Message, EpisodeResponse? Data)> CreateEpisodeAsync(
        Guid creatorId, Guid storyId, CreateEpisodeRequest req)
    {
        await using var conn = await _db.CreateConnectionAsync();

        var storyExists = await DbHelper.ExecuteScalarAsync<int>(conn,
            "SELECT COUNT(1) FROM stories WHERE id=@id AND creator_id=@cid AND deleted_at IS NULL",
            new() { ["@id"] = storyId, ["@cid"] = creatorId });
        if (storyExists == 0) return (false, "Story nahi mili ya permission nahi", null);

        var episodeNumber = await DbHelper.ExecuteScalarAsync<int>(conn,
            "SELECT COALESCE(MAX(episode_number), 0) + 1 FROM episodes WHERE story_id=@id AND deleted_at IS NULL",
            new() { ["@id"] = storyId });

        var episodeId = Guid.NewGuid();
        var slug = $"episode-{episodeNumber}-{GenerateSlug(req.Title)}";
        var wordCount = req.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var readTime = (int)Math.Ceiling(wordCount / 200.0) * 60; // 200 words/min
        var plainText = Regex.Replace(req.Content, "<.*?>", "");

        await DbHelper.ExecuteNonQueryAsync(conn, @"
            INSERT INTO episodes (id, story_id, title, slug, content, content_plain, word_count,
                estimated_read_time_seconds, episode_number, access_type, unlock_coin_cost,
                status, scheduled_publish_at, created_at, updated_at)
            VALUES (@id, @sid, @title, @slug, @content, @plain, @wc,
                @rt, @num, @access::episode_access, @cost,
                'draft'::story_status, @scheduled, NOW(), NOW())",
            new()
            {
                ["@id"]        = episodeId,
                ["@sid"]       = storyId,
                ["@title"]     = req.Title.Trim(),
                ["@slug"]      = slug,
                ["@content"]   = req.Content,
                ["@plain"]     = plainText,
                ["@wc"]        = wordCount,
                ["@rt"]        = readTime,
                ["@num"]       = episodeNumber,
                ["@access"]    = req.AccessType.ToLower(),
                ["@cost"]      = req.UnlockCoinCost,
                ["@scheduled"] = (object?)req.ScheduledPublishAt ?? DBNull.Value
            });

        // Update story episode count
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE stories SET total_episodes=total_episodes+1, updated_at=NOW() WHERE id=@id",
            new() { ["@id"] = storyId });

        var episode = await GetEpisodeAsync(storyId, episodeId, creatorId);
        return (true, "Episode create ho gaya!", episode);
    }

    // ─── UPDATE EPISODE ───────────────────────────────────────────────────────
    public async Task<(bool Success, string Message, EpisodeResponse? Data)> UpdateEpisodeAsync(
        Guid creatorId, Guid storyId, Guid episodeId, UpdateEpisodeRequest req)
    {
        await using var conn = await _db.CreateConnectionAsync();

        var exists = await DbHelper.ExecuteScalarAsync<int>(conn, @"
            SELECT COUNT(1) FROM episodes e
            JOIN stories s ON s.id=e.story_id
            WHERE e.id=@eid AND e.story_id=@sid AND s.creator_id=@cid AND e.deleted_at IS NULL",
            new() { ["@eid"] = episodeId, ["@sid"] = storyId, ["@cid"] = creatorId });
        if (exists == 0) return (false, "Episode nahi mila ya permission nahi", null);

        var updates = new List<string>();
        var p = new Dictionary<string, object?> { ["@id"] = episodeId };

        if (req.Title != null) { updates.Add("title=@title"); p["@title"] = req.Title.Trim(); }
        if (req.AccessType != null) { updates.Add("access_type=@access::episode_access"); p["@access"] = req.AccessType.ToLower(); }
        if (req.UnlockCoinCost != null) { updates.Add("unlock_coin_cost=@cost"); p["@cost"] = req.UnlockCoinCost; }
        if (req.ScheduledPublishAt != null) { updates.Add("scheduled_publish_at=@sched"); p["@sched"] = req.ScheduledPublishAt; }

        if (req.Content != null)
        {
            var wordCount = req.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            var readTime = (int)Math.Ceiling(wordCount / 200.0) * 60;
            var plainText = Regex.Replace(req.Content, "<.*?>", "");
            updates.Add("content=@content"); p["@content"] = req.Content;
            updates.Add("content_plain=@plain"); p["@plain"] = plainText;
            updates.Add("word_count=@wc"); p["@wc"] = wordCount;
            updates.Add("estimated_read_time_seconds=@rt"); p["@rt"] = readTime;
        }

        if (updates.Count == 0) return (false, "Kuch bhi update nahi kiya", null);

        updates.Add("updated_at=NOW()");
        await DbHelper.ExecuteNonQueryAsync(conn,
            $"UPDATE episodes SET {string.Join(",", updates)} WHERE id=@id", p);

        var episode = await GetEpisodeAsync(storyId, episodeId, creatorId);
        return (true, "Episode update ho gaya!", episode);
    }

    // ─── DELETE EPISODE ───────────────────────────────────────────────────────
    public async Task<(bool Success, string Message)> DeleteEpisodeAsync(
        Guid creatorId, Guid storyId, Guid episodeId)
    {
        await using var conn = await _db.CreateConnectionAsync();
        var rows = await DbHelper.ExecuteNonQueryAsync(conn, @"
            UPDATE episodes SET deleted_at=NOW() WHERE id=@eid AND story_id=@sid
            AND story_id IN (SELECT id FROM stories WHERE creator_id=@cid)",
            new() { ["@eid"] = episodeId, ["@sid"] = storyId, ["@cid"] = creatorId });

        if (rows > 0)
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE stories SET total_episodes=GREATEST(total_episodes-1,0), updated_at=NOW() WHERE id=@id",
                new() { ["@id"] = storyId });

        return rows > 0 ? (true, "Episode delete ho gaya!") : (false, "Episode nahi mila");
    }

    // ─── PUBLISH EPISODE ──────────────────────────────────────────────────────
    public async Task<(bool Success, string Message)> PublishEpisodeAsync(
        Guid creatorId, Guid storyId, Guid episodeId)
    {
        await using var conn = await _db.CreateConnectionAsync();
        var rows = await DbHelper.ExecuteNonQueryAsync(conn, @"
            UPDATE episodes SET status='published'::story_status,
                published_at=COALESCE(published_at, NOW()), updated_at=NOW()
            WHERE id=@eid AND story_id=@sid
            AND story_id IN (SELECT id FROM stories WHERE creator_id=@cid AND deleted_at IS NULL)",
            new() { ["@eid"] = episodeId, ["@sid"] = storyId, ["@cid"] = creatorId });

        return rows > 0 ? (true, "Episode publish ho gaya! 🎉") : (false, "Episode nahi mila");
    }

    // ─── GET EPISODE ──────────────────────────────────────────────────────────
    // ─── GET EPISODES LIST ────────────────────────────────────────────────────
    public async Task<List<EpisodeResponse>> GetEpisodesAsync(Guid storyId, Guid? viewerId)
    {
        var episodes = new List<EpisodeResponse>();

        await using var conn = await _db.CreateConnectionAsync();
        using var cmd = new NpgsqlCommand(@"
            SELECT id, story_id, title, slug, episode_number, access_type,
                   unlock_coin_cost, status, word_count, estimated_read_time_seconds,
                   total_views, total_likes, total_comments, content,
                   published_at, created_at
            FROM episodes
            WHERE story_id=@sid AND deleted_at IS NULL AND status='published'::story_status
            ORDER BY episode_number ASC", conn);
        cmd.Parameters.AddWithValue("@sid", storyId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            episodes.Add(MapToEpisode(reader, includeContent: false));

        // If viewer is the creator, show all (including drafts) - use separate query
        if (viewerId.HasValue)
        {
            await using var conn2 = await _db.CreateConnectionAsync();
            var isCreator = await DbHelper.ExecuteScalarAsync<int>(conn2,
                "SELECT COUNT(1) FROM stories WHERE id=@sid AND creator_id=@uid",
                new() { ["@sid"] = storyId, ["@uid"] = viewerId });

            if (isCreator > 0)
            {
                // Creator: show all episodes including drafts
                episodes.Clear();
                await using var conn3 = await _db.CreateConnectionAsync();
                using var cmd3 = new NpgsqlCommand(@"
                    SELECT id, story_id, title, slug, episode_number, access_type,
                           unlock_coin_cost, status, word_count, estimated_read_time_seconds,
                           total_views, total_likes, total_comments, content,
                           published_at, created_at
                    FROM episodes
                    WHERE story_id=@sid AND deleted_at IS NULL
                    ORDER BY episode_number ASC", conn3);
                cmd3.Parameters.AddWithValue("@sid", storyId);

                using var r3 = await cmd3.ExecuteReaderAsync();
                while (await r3.ReadAsync())
                    episodes.Add(MapToEpisode(r3, includeContent: false));
            }
        }

        return episodes;
    }

    public async Task<EpisodeResponse?> GetEpisodeAsync(Guid storyId, Guid episodeId, Guid? viewerId)
    {
        EpisodeResponse? episode = null;

        await using (var conn = await _db.CreateConnectionAsync())
        {
            using var cmd = new NpgsqlCommand(@"
                SELECT id, story_id, title, slug, episode_number, access_type,
                       unlock_coin_cost, status, word_count, estimated_read_time_seconds,
                       total_views, total_likes, total_comments, content,
                       published_at, created_at
                FROM episodes
                WHERE id=@eid AND story_id=@sid AND deleted_at IS NULL", conn);
            cmd.Parameters.AddWithValue("@eid", episodeId);
            cmd.Parameters.AddWithValue("@sid", storyId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                episode = MapToEpisode(reader, includeContent: true);
        }

        if (episode == null) return null;

        // Check access
        if (episode.AccessType == "premium" && viewerId.HasValue)
        {
            await using var conn2 = await _db.CreateConnectionAsync();

            // Creator khud dekh sakta hai
            var isCreator = await DbHelper.ExecuteScalarAsync<int>(conn2,
                "SELECT COUNT(1) FROM stories WHERE id=@sid AND creator_id=@uid",
                new() { ["@sid"] = storyId, ["@uid"] = viewerId });

            if (isCreator > 0)
            {
                episode.IsUnlocked = true;
            }
            else
            {
                var unlocked = await DbHelper.ExecuteScalarAsync<int>(conn2,
                    "SELECT COUNT(1) FROM episode_unlocks WHERE user_id=@uid AND episode_id=@eid",
                    new() { ["@uid"] = viewerId, ["@eid"] = episodeId });
                episode.IsUnlocked = unlocked > 0;
                if (!episode.IsUnlocked)
                    episode.Content = null; // Premium content hide karo
            }
        }
        else if (episode.AccessType == "free")
        {
            episode.IsUnlocked = true;
        }

        return episode;
    }

    // ─── LIKE STORY ───────────────────────────────────────────────────────────
    public async Task<(bool Success, string Message)> LikeStoryAsync(Guid userId, Guid storyId)
    {
        await using var conn = await _db.CreateConnectionAsync();
        var exists = await DbHelper.ExecuteScalarAsync<int>(conn,
            "SELECT COUNT(1) FROM story_likes WHERE user_id=@uid AND story_id=@sid",
            new() { ["@uid"] = userId, ["@sid"] = storyId });
        if (exists > 0) return (false, "Already like kiya hua hai");

        await DbHelper.ExecuteNonQueryAsync(conn,
            "INSERT INTO story_likes (id,user_id,story_id,created_at) VALUES (uuid_generate_v4(),@uid,@sid,NOW())",
            new() { ["@uid"] = userId, ["@sid"] = storyId });
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE stories SET total_likes=total_likes+1 WHERE id=@id",
            new() { ["@id"] = storyId });

        return (true, "Story like ho gayi! 👻");
    }

    // ─── UNLIKE STORY ─────────────────────────────────────────────────────────
    public async Task<(bool Success, string Message)> UnlikeStoryAsync(Guid userId, Guid storyId)
    {
        await using var conn = await _db.CreateConnectionAsync();
        var rows = await DbHelper.ExecuteNonQueryAsync(conn,
            "DELETE FROM story_likes WHERE user_id=@uid AND story_id=@sid",
            new() { ["@uid"] = userId, ["@sid"] = storyId });
        if (rows == 0) return (false, "Like nahi kiya tha");
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE stories SET total_likes=GREATEST(total_likes-1,0) WHERE id=@id",
            new() { ["@id"] = storyId });
        return (true, "Unlike ho gaya");
    }

    // ─── BOOKMARK STORY ───────────────────────────────────────────────────────
    public async Task<(bool Success, string Message)> BookmarkStoryAsync(Guid userId, Guid storyId)
    {
        await using var conn = await _db.CreateConnectionAsync();
        var exists = await DbHelper.ExecuteScalarAsync<int>(conn,
            "SELECT COUNT(1) FROM bookmarks WHERE user_id=@uid AND story_id=@sid",
            new() { ["@uid"] = userId, ["@sid"] = storyId });
        if (exists > 0) return (false, "Already bookmark hai");

        await DbHelper.ExecuteNonQueryAsync(conn,
            "INSERT INTO bookmarks (id,user_id,story_id,created_at) VALUES (uuid_generate_v4(),@uid,@sid,NOW())",
            new() { ["@uid"] = userId, ["@sid"] = storyId });
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE stories SET total_bookmarks=total_bookmarks+1 WHERE id=@id",
            new() { ["@id"] = storyId });

        return (true, "Bookmark ho gaya! 🔖");
    }

    // ─── UNBOOKMARK STORY ─────────────────────────────────────────────────────
    public async Task<(bool Success, string Message)> UnbookmarkStoryAsync(Guid userId, Guid storyId)
    {
        await using var conn = await _db.CreateConnectionAsync();
        var rows = await DbHelper.ExecuteNonQueryAsync(conn,
            "DELETE FROM bookmarks WHERE user_id=@uid AND story_id=@sid",
            new() { ["@uid"] = userId, ["@sid"] = storyId });
        if (rows == 0) return (false, "Bookmark nahi tha");
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE stories SET total_bookmarks=GREATEST(total_bookmarks-1,0) WHERE id=@id",
            new() { ["@id"] = storyId });
        return (true, "Bookmark hata diya");
    }

    // ─── GET BOOKMARKED STORIES ───────────────────────────────────────────────
    public async Task<PagedResult<StoryResponse>> GetBookmarkedStoriesAsync(
        Guid userId, int page, int pageSize)
    {
        pageSize = Math.Min(pageSize, 50);
        var offset = (page - 1) * pageSize;
        var results = new List<StoryResponse>();
        int total = 0;

        await using var conn = await _db.CreateConnectionAsync();
        total = await DbHelper.ExecuteScalarAsync<int>(conn,
            "SELECT COUNT(1) FROM bookmarks WHERE user_id=@uid",
            new() { ["@uid"] = userId });

        using var cmd = new NpgsqlCommand(@"
            SELECT s.id, s.title, s.slug, s.summary, s.thumbnail_url, s.story_type,
                   s.language, s.age_rating, s.status, s.creator_id, s.category_id,
                   s.total_episodes, s.total_views, s.total_likes, s.total_comments,
                   s.total_bookmarks, s.engagement_score, s.is_editor_pick,
                   s.published_at, s.created_at, s.updated_at,
                   u.username as creator_username, u.display_name as creator_display_name,
                   u.avatar_url as creator_avatar_url, u.is_verified_creator,
                   c.name as category_name
            FROM bookmarks b
            JOIN stories s ON s.id=b.story_id
            JOIN users u ON u.id=s.creator_id
            LEFT JOIN categories c ON c.id=s.category_id
            WHERE b.user_id=@uid AND s.deleted_at IS NULL
            ORDER BY b.created_at DESC LIMIT @lim OFFSET @off", conn);
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@lim", pageSize);
        cmd.Parameters.AddWithValue("@off", offset);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var s = MapToStory(reader);
            s.IsBookmarked = true;
            results.Add(s);
        }

        return new PagedResult<StoryResponse>
        {
            Items = results, TotalCount = total, Page = page, PageSize = pageSize
        };
    }

    // ─── RECORD VIEW ──────────────────────────────────────────────────────────
    public async Task RecordViewAsync(Guid storyId, Guid? episodeId, Guid? userId,
        string ipAddress, string? userAgent)
    {
        try
        {
            await using var conn = await _db.CreateConnectionAsync();

            using var cmd = new NpgsqlCommand(@"
                INSERT INTO story_views (id, story_id, episode_id, user_id, ip_address, user_agent, viewed_at)
                VALUES (uuid_generate_v4(), @sid, @eid, @uid, @ip, @ua, NOW())", conn);
            cmd.Parameters.AddWithValue("@sid", storyId);
            cmd.Parameters.AddWithValue("@eid", (object?)episodeId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@uid", (object?)userId ?? DBNull.Value);
            cmd.Parameters.Add(new NpgsqlParameter("@ip", NpgsqlTypes.NpgsqlDbType.Inet)
            {
                Value = System.Net.IPAddress.TryParse(ipAddress, out var ip)
                    ? ip : System.Net.IPAddress.Loopback
            });
            cmd.Parameters.AddWithValue("@ua", (object?)userAgent ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();

            // Update counters
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE stories SET total_views=total_views+1 WHERE id=@id",
                new() { ["@id"] = storyId });

            if (episodeId.HasValue)
                await DbHelper.ExecuteNonQueryAsync(conn,
                    "UPDATE episodes SET total_views=total_views+1 WHERE id=@id",
                    new() { ["@id"] = episodeId });
        }
        catch { }
    }

    // ─── UNLOCK EPISODE ───────────────────────────────────────────────────────
    public async Task<(bool Success, string Message)> UnlockEpisodeAsync(Guid userId, Guid episodeId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        // Already unlocked?
        var alreadyUnlocked = await DbHelper.ExecuteScalarAsync<int>(conn,
            "SELECT COUNT(1) FROM episode_unlocks WHERE user_id=@uid AND episode_id=@eid",
            new() { ["@uid"] = userId, ["@eid"] = episodeId });
        if (alreadyUnlocked > 0) return (false, "Episode pehle se unlock hai");

        // Episode details
        int coinCost = 0;
        Guid storyId = Guid.Empty;
        bool found = false;

        await using (var rc = await _db.CreateConnectionAsync())
        {
            using var cmd = new NpgsqlCommand(
                "SELECT story_id, unlock_coin_cost, access_type FROM episodes WHERE id=@id AND deleted_at IS NULL",
                rc);
            cmd.Parameters.AddWithValue("@id", episodeId);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                storyId = DbHelper.GetGuid(reader, "story_id");
                coinCost = DbHelper.GetInt(reader, "unlock_coin_cost");
                var accessType = DbHelper.GetString(reader, "access_type");
                found = true;
                if (accessType == "free") return (false, "Yeh episode free hai, unlock ki zarurat nahi");
            }
        }

        if (!found) return (false, "Episode nahi mila");

        // Check balance
        var balance = await DbHelper.ExecuteScalarAsync<long>(conn,
            "SELECT coin_balance FROM wallets WHERE user_id=@uid",
            new() { ["@uid"] = userId });
        if (balance < coinCost)
            return (false, $"Insufficient coins. {coinCost} coins chahiye, tumhare paas {balance} hain");

        await using var tx = await conn.BeginTransactionAsync();
        bool committed = false;
        try
        {
            // Deduct coins
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE wallets SET coin_balance=coin_balance-@cost, updated_at=NOW() WHERE user_id=@uid",
                new() { ["@cost"] = coinCost, ["@uid"] = userId }, tx);

            // Record unlock
            await DbHelper.ExecuteNonQueryAsync(conn, @"
                INSERT INTO episode_unlocks (id, user_id, episode_id, story_id, coins_spent, unlocked_at)
                VALUES (uuid_generate_v4(), @uid, @eid, @sid, @cost, NOW())",
                new() { ["@uid"] = userId, ["@eid"] = episodeId, ["@sid"] = storyId, ["@cost"] = coinCost }, tx);

            // Coin transaction record
            await DbHelper.ExecuteNonQueryAsync(conn, @"
                INSERT INTO coin_transactions (id, sender_id, transaction_type, amount, description, status, created_at)
                VALUES (uuid_generate_v4(), @uid, 'episode_unlock'::transaction_type, @cost, 'Episode unlock', 'completed'::transaction_status, NOW())",
                new() { ["@uid"] = userId, ["@cost"] = coinCost }, tx);

            await tx.CommitAsync();
            committed = true;
        }
        catch (Exception ex)
        {
            if (!committed) try { await tx.RollbackAsync(); } catch { }
            _logger.LogError(ex, "Unlock episode failed");
            return (false, "Unlock failed");
        }

        return (true, $"Episode unlock ho gaya! {coinCost} coins use hue. 🔓");
    }

    // ─── GET CATEGORIES ───────────────────────────────────────────────────────
    public async Task<List<CategoryResponse>> GetCategoriesAsync()
    {
        var results = new List<CategoryResponse>();
        await using var conn = await _db.CreateConnectionAsync();

        using var cmd = new NpgsqlCommand(
            "SELECT id, name, slug, description, icon_url FROM categories WHERE is_active=TRUE ORDER BY display_order ASC, name ASC",
            conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(new CategoryResponse
            {
                Id          = DbHelper.GetGuid(reader, "id"),
                Name        = DbHelper.GetString(reader, "name"),
                Slug        = DbHelper.GetStringOrNull(reader, "slug"),
                Description = DbHelper.GetStringOrNull(reader, "description"),
                IconUrl     = DbHelper.GetStringOrNull(reader, "icon_url")
            });

        return results;
    }

    // ─── RATE STORY ───────────────────────────────────────────────────────────
    public async Task<(bool Success, string Message, object? Data)> RateStoryAsync(Guid userId, Guid storyId, int rating)
    {
        if (rating < 1 || rating > 5) return (false, "Rating 1-5 ke beech honi chahiye", null);
        await using var conn = await _db.CreateConnectionAsync();

        // UPSERT rating
        await DbHelper.ExecuteNonQueryAsync(conn,
            @"INSERT INTO story_ratings (id, user_id, story_id, rating, created_at, updated_at)
              VALUES (@id, @uid, @sid, @rating, NOW(), NOW())
              ON CONFLICT (user_id, story_id) DO UPDATE SET rating=@rating, updated_at=NOW()",
            new() { ["@id"] = Guid.NewGuid(), ["@uid"] = userId, ["@sid"] = storyId, ["@rating"] = rating });

        // Get new average
        var avgRating = await DbHelper.ExecuteScalarAsync<decimal>(conn,
            "SELECT ROUND(AVG(rating)::numeric, 1) FROM story_ratings WHERE story_id=@sid",
            new() { ["@sid"] = storyId });
        var ratingCount = await DbHelper.ExecuteScalarAsync<int>(conn,
            "SELECT COUNT(1) FROM story_ratings WHERE story_id=@sid",
            new() { ["@sid"] = storyId });

        return (true, "Rating de di! ⭐", new { average_rating = (double)avgRating, rating_count = ratingCount, my_rating = rating });
    }

    // ─── CREATE CATEGORY ──────────────────────────────────────────────────────
    public async Task<(bool Success, string Message, CategoryResponse? Data)> CreateCategoryAsync(
        string name, string? description, string? iconUrl)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (false, "Category name required hai", null);

        name = name.Trim();
        var slug = System.Text.RegularExpressions.Regex.Replace(name.ToLower(), @"[^a-z0-9]+", "-").Trim('-');

        await using var conn = await _db.CreateConnectionAsync();

        // Check if already exists (case-insensitive)
        var existing = await DbHelper.ExecuteScalarAsync<int>(conn,
            "SELECT COUNT(1) FROM categories WHERE LOWER(name)=LOWER(@name)",
            new() { ["@name"] = name });
        if (existing > 0)
        {
            // Return the existing category instead of failing
            var cat = new CategoryResponse();
            using var cmd2 = new NpgsqlCommand(
                "SELECT id, name, slug, description, icon_url FROM categories WHERE LOWER(name)=LOWER(@name) LIMIT 1",
                conn);
            cmd2.Parameters.AddWithValue("@name", name);
            using var r2 = await cmd2.ExecuteReaderAsync();
            if (await r2.ReadAsync())
            {
                cat.Id = DbHelper.GetGuid(r2, "id");
                cat.Name = DbHelper.GetString(r2, "name");
                cat.Slug = DbHelper.GetStringOrNull(r2, "slug");
                cat.Description = DbHelper.GetStringOrNull(r2, "description");
                cat.IconUrl = DbHelper.GetStringOrNull(r2, "icon_url");
            }
            return (true, "Category already exist karti hai!", cat);
        }

        var id = Guid.NewGuid();
        await DbHelper.ExecuteNonQueryAsync(conn,
            @"INSERT INTO categories (id, name, slug, description, icon_url, is_active, display_order)
              VALUES (@id, @name, @slug, @desc, @icon, TRUE, 99)",
            new() { ["@id"] = id, ["@name"] = name, ["@slug"] = slug,
                    ["@desc"] = (object?)description ?? DBNull.Value,
                    ["@icon"] = (object?)iconUrl ?? DBNull.Value });

        return (true, "Category ban gayi! 🎉", new CategoryResponse
        {
            Id = id, Name = name, Slug = slug,
            Description = description, IconUrl = iconUrl
        });
    }

    // ─── DELETE CATEGORY ──────────────────────────────────────────────────────
    public async Task<(bool Success, string Message)> DeleteCategoryAsync(Guid categoryId)
    {
        await using var conn = await _db.CreateConnectionAsync();
        var rows = await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE categories SET is_active=FALSE WHERE id=@id",
            new() { ["@id"] = categoryId });
        return rows > 0 ? (true, "Category delete ho gayi!") : (false, "Category nahi mili");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────
    private async Task UpsertTagsAsync(NpgsqlConnection conn, Guid storyId,
        List<string> tags, NpgsqlTransaction? tx)
    {
        foreach (var tag in tags.Select(t => t.Trim().ToLower()).Where(t => !string.IsNullOrEmpty(t)).Distinct())
        {
            var tagId = await DbHelper.ExecuteScalarAsync<Guid?>(conn,
                "SELECT id FROM tags WHERE LOWER(name)=@name", new() { ["@name"] = tag });

            if (!tagId.HasValue)
            {
                tagId = Guid.NewGuid();
                await DbHelper.ExecuteNonQueryAsync(conn,
                    "INSERT INTO tags (id, name, slug, created_at) VALUES (@id, @name, @slug, NOW()) ON CONFLICT DO NOTHING",
                    new() { ["@id"] = tagId, ["@name"] = tag, ["@slug"] = GenerateSlug(tag) }, tx);
            }

            await DbHelper.ExecuteNonQueryAsync(conn,
                "INSERT INTO story_tags (story_id, tag_id) VALUES (@sid, @tid) ON CONFLICT DO NOTHING",
                new() { ["@sid"] = storyId, ["@tid"] = tagId }, tx);
        }
    }

    private async Task<string> GenerateUniqueSlugAsync(NpgsqlConnection conn, string title)
    {
        var baseSlug = GenerateSlug(title);
        var slug = baseSlug;
        int count = 0;
        while (true)
        {
            var exists = await DbHelper.ExecuteScalarAsync<int>(conn,
                "SELECT COUNT(1) FROM stories WHERE slug=@slug", new() { ["@slug"] = slug });
            if (exists == 0) return slug;
            slug = $"{baseSlug}-{++count}";
            if (count > 20) slug = $"{baseSlug}-{Guid.NewGuid().ToString("N")[..6]}";
            if (count > 25) break;
        }
        return slug;
    }

    private string GenerateSlug(string text)
    {
        text = text.ToLower().Trim();
        text = Regex.Replace(text, @"[^a-z0-9\s-]", "");
        text = Regex.Replace(text, @"\s+", "-");
        text = Regex.Replace(text, @"-+", "-").Trim('-');
        return text.Length > 100 ? text[..100] : text;
    }

    private StoryResponse MapToStory(NpgsqlDataReader r) => new()
    {
        Id                  = DbHelper.GetGuid(r, "id"),
        Title               = DbHelper.GetString(r, "title"),
        Slug                = DbHelper.GetString(r, "slug"),
        Summary             = DbHelper.GetStringOrNull(r, "summary"),
        ThumbnailUrl        = DbHelper.GetStringOrNull(r, "thumbnail_url"),
        StoryType           = DbHelper.GetString(r, "story_type"),
        Language            = DbHelper.GetString(r, "language"),
        AgeRating           = DbHelper.GetString(r, "age_rating"),
        Status              = DbHelper.GetString(r, "status"),
        CreatorId           = DbHelper.GetGuid(r, "creator_id"),
        CreatorUsername     = DbHelper.GetString(r, "creator_username"),
        CreatorDisplayName  = DbHelper.GetStringOrNull(r, "creator_display_name"),
        CreatorAvatarUrl    = DbHelper.GetStringOrNull(r, "creator_avatar_url"),
        IsVerifiedCreator   = DbHelper.GetBool(r, "is_verified_creator"),
        CategoryId          = r.IsDBNull(r.GetOrdinal("category_id")) ? null : DbHelper.GetGuid(r, "category_id"),
        CategoryName        = DbHelper.GetStringOrNull(r, "category_name"),
        TotalEpisodes       = DbHelper.GetInt(r, "total_episodes"),
        TotalViews          = DbHelper.GetLong(r, "total_views"),
        TotalLikes          = DbHelper.GetLong(r, "total_likes"),
        TotalComments       = DbHelper.GetLong(r, "total_comments"),
        TotalBookmarks      = DbHelper.GetLong(r, "total_bookmarks"),
        EngagementScore     = DbHelper.GetDecimal(r, "engagement_score"),
        IsEditorPick        = DbHelper.GetBool(r, "is_editor_pick"),
        PublishedAt         = DbHelper.GetDateTimeOrNull(r, "published_at"),
        CreatedAt           = DbHelper.GetDateTime(r, "created_at"),
        UpdatedAt           = DbHelper.GetDateTime(r, "updated_at")
    };

    private StoryDetailResponse MapToStoryDetail(NpgsqlDataReader r) => new()
    {
        Id                  = DbHelper.GetGuid(r, "id"),
        Title               = DbHelper.GetString(r, "title"),
        Slug                = DbHelper.GetString(r, "slug"),
        Summary             = DbHelper.GetStringOrNull(r, "summary"),
        ThumbnailUrl        = DbHelper.GetStringOrNull(r, "thumbnail_url"),
        StoryType           = DbHelper.GetString(r, "story_type"),
        Language            = DbHelper.GetString(r, "language"),
        AgeRating           = DbHelper.GetString(r, "age_rating"),
        Status              = DbHelper.GetString(r, "status"),
        CreatorId           = DbHelper.GetGuid(r, "creator_id"),
        CreatorUsername     = DbHelper.GetString(r, "creator_username"),
        CreatorDisplayName  = DbHelper.GetStringOrNull(r, "creator_display_name"),
        CreatorAvatarUrl    = DbHelper.GetStringOrNull(r, "creator_avatar_url"),
        IsVerifiedCreator   = DbHelper.GetBool(r, "is_verified_creator"),
        CategoryId          = r.IsDBNull(r.GetOrdinal("category_id")) ? null : DbHelper.GetGuid(r, "category_id"),
        CategoryName        = DbHelper.GetStringOrNull(r, "category_name"),
        TotalEpisodes       = DbHelper.GetInt(r, "total_episodes"),
        TotalViews          = DbHelper.GetLong(r, "total_views"),
        TotalLikes          = DbHelper.GetLong(r, "total_likes"),
        TotalComments       = DbHelper.GetLong(r, "total_comments"),
        TotalBookmarks      = DbHelper.GetLong(r, "total_bookmarks"),
        EngagementScore     = DbHelper.GetDecimal(r, "engagement_score"),
        IsEditorPick        = DbHelper.GetBool(r, "is_editor_pick"),
        PublishedAt         = DbHelper.GetDateTimeOrNull(r, "published_at"),
        CreatedAt           = DbHelper.GetDateTime(r, "created_at"),
        UpdatedAt           = DbHelper.GetDateTime(r, "updated_at")
    };

    private EpisodeResponse MapToEpisode(NpgsqlDataReader r, bool includeContent) => new()
    {
        Id                       = DbHelper.GetGuid(r, "id"),
        StoryId                  = DbHelper.GetGuid(r, "story_id"),
        Title                    = DbHelper.GetString(r, "title"),
        Slug                     = DbHelper.GetString(r, "slug"),
        EpisodeNumber            = DbHelper.GetInt(r, "episode_number"),
        AccessType               = DbHelper.GetString(r, "access_type"),
        UnlockCoinCost           = DbHelper.GetInt(r, "unlock_coin_cost"),
        Status                   = DbHelper.GetString(r, "status"),
        WordCount                = DbHelper.GetInt(r, "word_count"),
        EstimatedReadTimeSeconds = DbHelper.GetInt(r, "estimated_read_time_seconds"),
        TotalViews               = DbHelper.GetLong(r, "total_views"),
        TotalLikes               = DbHelper.GetLong(r, "total_likes"),
        TotalComments            = DbHelper.GetLong(r, "total_comments"),
        Content                  = includeContent ? DbHelper.GetStringOrNull(r, "content") : null,
        PublishedAt              = DbHelper.GetDateTimeOrNull(r, "published_at"),
        CreatedAt                = DbHelper.GetDateTime(r, "created_at")
    };
}