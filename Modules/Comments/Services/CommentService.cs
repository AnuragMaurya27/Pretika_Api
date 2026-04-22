using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Infrastructure.Database;
using HauntedVoiceUniverse.Modules.Comments.Models;
using HauntedVoiceUniverse.Modules.Notifications.Services;
using Npgsql;

namespace HauntedVoiceUniverse.Modules.Comments.Services;

public interface ICommentService
{
    Task<PagedResult<CommentResponse>> GetStoryCommentsAsync(Guid storyId, Guid? viewerId, int page, int pageSize);
    Task<PagedResult<CommentResponse>> GetRepliesAsync(Guid commentId, Guid? viewerId, int page, int pageSize);
    Task<(bool Success, string Message, CommentResponse? Data)> CreateCommentAsync(Guid userId, CreateCommentRequest req);
    Task<(bool Success, string Message)> DeleteCommentAsync(Guid userId, Guid commentId);
    Task<(bool Success, string Message)> LikeCommentAsync(Guid userId, Guid commentId);
    Task<(bool Success, string Message)> UnlikeCommentAsync(Guid userId, Guid commentId);
    Task<(bool Success, string Message)> ReportCommentAsync(Guid userId, Guid commentId, ReportCommentRequest req);
    Task<(bool Success, string Message)> PinCommentAsync(Guid requesterId, Guid commentId);
    Task<(bool Success, string Message)> UnpinCommentAsync(Guid requesterId, Guid commentId);
}

public class CommentService : ICommentService
{
    private readonly IDbConnectionFactory _db;
    private readonly INotificationService _notifications;

    public CommentService(IDbConnectionFactory db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    private static CommentResponse MapComment(NpgsqlDataReader r, Guid? viewerId)
    {
        return new CommentResponse
        {
            Id = DbHelper.GetGuid(r, "id"),
            StoryId = DbHelper.GetGuid(r, "story_id"),
            EpisodeId = r.IsDBNull(r.GetOrdinal("episode_id")) ? null : DbHelper.GetGuid(r, "episode_id"),
            ParentCommentId = r.IsDBNull(r.GetOrdinal("parent_comment_id")) ? null : DbHelper.GetGuid(r, "parent_comment_id"),
            Content = DbHelper.GetString(r, "content"),
            LikesCount = DbHelper.GetInt(r, "likes_count"),
            RepliesCount = DbHelper.GetInt(r, "replies_count"),
            IsPinned = DbHelper.GetBool(r, "is_pinned"),
            IsCreatorReply = DbHelper.GetBool(r, "is_creator_reply"),
            CreatedAt = DbHelper.GetDateTime(r, "created_at"),
            UpdatedAt = DbHelper.GetDateTime(r, "updated_at"),
            AuthorId = DbHelper.GetGuid(r, "user_id"),
            AuthorUsername = DbHelper.GetString(r, "username"),
            AuthorDisplayName = DbHelper.GetStringOrNull(r, "display_name"),
            AuthorAvatarUrl = DbHelper.GetStringOrNull(r, "avatar_url"),
            AuthorIsCreator = DbHelper.GetBool(r, "author_is_creator"),
            IsLikedByMe = DbHelper.GetBool(r, "is_liked_by_me"),
            IsMyComment = viewerId.HasValue && DbHelper.GetGuid(r, "user_id") == viewerId.Value,
            UserRating = r.IsDBNull(r.GetOrdinal("user_rating")) ? null : (int?)r.GetInt32(r.GetOrdinal("user_rating"))
        };
    }

    public async Task<PagedResult<CommentResponse>> GetStoryCommentsAsync(Guid storyId, Guid? viewerId, int page, int pageSize)
    {
        using var conn = await _db.CreateConnectionAsync();
        pageSize = Math.Min(pageSize, 50);
        int offset = (page - 1) * pageSize;

        var countSql = @"SELECT COUNT(*) FROM comments WHERE story_id = @storyId AND parent_comment_id IS NULL AND deleted_at IS NULL AND is_hidden = FALSE";
        var total = await DbHelper.ExecuteScalarAsync<long>(conn, countSql, new Dictionary<string, object?> { ["storyId"] = storyId });

        var sql = @"
            SELECT c.id, c.story_id, c.episode_id, c.parent_comment_id, c.content,
                   c.likes_count, c.replies_count, c.is_pinned, c.is_creator_reply,
                   c.created_at, c.updated_at, c.user_id,
                   u.username, u.display_name, u.avatar_url, u.is_creator as author_is_creator,
                   CASE WHEN cl.id IS NOT NULL THEN TRUE ELSE FALSE END as is_liked_by_me,
                   sr.rating as user_rating
            FROM comments c
            JOIN users u ON u.id = c.user_id
            LEFT JOIN comment_likes cl ON cl.comment_id = c.id AND cl.user_id = @viewerId
            LEFT JOIN story_ratings sr ON sr.user_id = c.user_id AND sr.story_id = c.story_id
            WHERE c.story_id = @storyId AND c.parent_comment_id IS NULL
              AND c.deleted_at IS NULL AND c.is_hidden = FALSE
            ORDER BY c.is_pinned DESC, c.created_at DESC
            LIMIT @limit OFFSET @offset";

        var items = await DbHelper.ExecuteReaderAsync(conn, sql, r => MapComment(r, viewerId),
            new Dictionary<string, object?>
            {
                ["storyId"] = storyId,
                ["viewerId"] = (object?)viewerId ?? DBNull.Value,
                ["limit"] = pageSize,
                ["offset"] = offset
            });

        return PagedResult<CommentResponse>.Create(items, (int)total, page, pageSize);
    }

    public async Task<PagedResult<CommentResponse>> GetRepliesAsync(Guid commentId, Guid? viewerId, int page, int pageSize)
    {
        using var conn = await _db.CreateConnectionAsync();
        pageSize = Math.Min(pageSize, 50);
        int offset = (page - 1) * pageSize;

        var countSql = "SELECT COUNT(*) FROM comments WHERE parent_comment_id = @commentId AND deleted_at IS NULL AND is_hidden = FALSE";
        var total = await DbHelper.ExecuteScalarAsync<long>(conn, countSql, new Dictionary<string, object?> { ["commentId"] = commentId });

        var sql = @"
            SELECT c.id, c.story_id, c.episode_id, c.parent_comment_id, c.content,
                   c.likes_count, c.replies_count, c.is_pinned, c.is_creator_reply,
                   c.created_at, c.updated_at, c.user_id,
                   u.username, u.display_name, u.avatar_url, u.is_creator as author_is_creator,
                   CASE WHEN cl.id IS NOT NULL THEN TRUE ELSE FALSE END as is_liked_by_me,
                   sr.rating as user_rating
            FROM comments c
            JOIN users u ON u.id = c.user_id
            LEFT JOIN comment_likes cl ON cl.comment_id = c.id AND cl.user_id = @viewerId
            LEFT JOIN story_ratings sr ON sr.user_id = c.user_id AND sr.story_id = c.story_id
            WHERE c.parent_comment_id = @commentId
              AND c.deleted_at IS NULL AND c.is_hidden = FALSE
            ORDER BY c.created_at ASC
            LIMIT @limit OFFSET @offset";

        var items = await DbHelper.ExecuteReaderAsync(conn, sql, r => MapComment(r, viewerId),
            new Dictionary<string, object?>
            {
                ["commentId"] = commentId,
                ["viewerId"] = (object?)viewerId ?? DBNull.Value,
                ["limit"] = pageSize,
                ["offset"] = offset
            });

        return PagedResult<CommentResponse>.Create(items, (int)total, page, pageSize);
    }

    public async Task<(bool Success, string Message, CommentResponse? Data)> CreateCommentAsync(Guid userId, CreateCommentRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();

        // Story exists check
        var storyExists = await DbHelper.ExecuteScalarAsync<bool>(conn,
            "SELECT EXISTS(SELECT 1 FROM stories WHERE id = @storyId AND status = 'published' AND deleted_at IS NULL)",
            new Dictionary<string, object?> { ["storyId"] = req.StoryId });
        if (!storyExists) return (false, "Story nahi mili", null);

        // Parent comment exists check (if reply)
        if (req.ParentCommentId.HasValue)
        {
            var parentExists = await DbHelper.ExecuteScalarAsync<bool>(conn,
                "SELECT EXISTS(SELECT 1 FROM comments WHERE id = @pid AND deleted_at IS NULL)",
                new Dictionary<string, object?> { ["pid"] = req.ParentCommentId.Value });
            if (!parentExists) return (false, "Parent comment nahi mila", null);
        }

        // Is creator replying?
        bool isCreatorReply = false;
        if (req.ParentCommentId.HasValue)
        {
            isCreatorReply = await DbHelper.ExecuteScalarAsync<bool>(conn,
                "SELECT EXISTS(SELECT 1 FROM stories WHERE id = @storyId AND creator_id = @userId)",
                new Dictionary<string, object?> { ["storyId"] = req.StoryId, ["userId"] = userId });
        }

        // BUG#M9-5 FIX: Duplicate/spam comment filter.
        // (a) Same user same content on same story within 5 minutes → reject.
        var isDuplicate = await DbHelper.ExecuteScalarAsync<bool>(conn,
            @"SELECT EXISTS(
                SELECT 1 FROM comments
                WHERE user_id = @userId AND story_id = @storyId
                  AND LOWER(content) = LOWER(@content)
                  AND deleted_at IS NULL
                  AND created_at > NOW() - INTERVAL '5 minutes'
              )",
            new Dictionary<string, object?>
            {
                ["userId"] = userId, ["storyId"] = req.StoryId, ["content"] = req.Content
            });
        if (isDuplicate) return (false, "Duplicate comment nahi kar sakte. 5 minute baad try karo.", null);

        // (b) Same user posted ≥5 comments on same story in last 2 minutes → spam rate limit.
        var recentCount = await DbHelper.ExecuteScalarAsync<int>(conn,
            @"SELECT COUNT(1) FROM comments
              WHERE user_id = @userId AND story_id = @storyId
                AND deleted_at IS NULL
                AND created_at > NOW() - INTERVAL '2 minutes'",
            new Dictionary<string, object?> { ["userId"] = userId, ["storyId"] = req.StoryId });
        if (recentCount >= 5)
            return (false, "Bahut zyada comments. 2 minute ruko.", null);

        // (c) URL/link spam — reject if comment contains more than 2 URLs.
        var urlCount = System.Text.RegularExpressions.Regex.Matches(
            req.Content, @"https?://\S+", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
        if (urlCount > 2)
            return (false, "Comment mein zyada links allowed nahi hain.", null);

        var id = Guid.NewGuid();
        await DbHelper.ExecuteNonQueryAsync(conn,
            @"INSERT INTO comments (id, user_id, story_id, episode_id, parent_comment_id, content, is_creator_reply)
              VALUES (@id, @userId, @storyId, @episodeId, @parentId, @content, @isCreatorReply)",
            new Dictionary<string, object?>
            {
                ["id"] = id,
                ["userId"] = userId,
                ["storyId"] = req.StoryId,
                ["episodeId"] = (object?)req.EpisodeId ?? DBNull.Value,
                ["parentId"] = (object?)req.ParentCommentId ?? DBNull.Value,
                ["content"] = req.Content,
                ["isCreatorReply"] = isCreatorReply
            });

        // Update parent replies_count
        if (req.ParentCommentId.HasValue)
        {
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE comments SET replies_count = replies_count + 1 WHERE id = @pid",
                new Dictionary<string, object?> { ["pid"] = req.ParentCommentId.Value });
        }

        // Update story total_comments
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE stories SET total_comments = total_comments + 1 WHERE id = @storyId",
            new Dictionary<string, object?> { ["storyId"] = req.StoryId });

        // Grant +5 reader XP for posting a comment and update reader_fear_rank.
        await DbHelper.ExecuteNonQueryAsync(conn, @"
            UPDATE users SET
                reader_rank_score = reader_rank_score + 5,
                reader_fear_rank  = CASE
                    WHEN reader_rank_score + 5 >= 10000 THEN 'mahakaal_bhakt'
                    WHEN reader_rank_score + 5 >= 4000  THEN 'horror_bhakt'
                    WHEN reader_rank_score + 5 >= 1500  THEN 'shamshaan_premi'
                    WHEN reader_rank_score + 5 >= 500   THEN 'andheri_gali_explorer'
                    ELSE 'raat_ka_musafir'
                END
            WHERE id = @uid",
            new Dictionary<string, object?> { ["uid"] = userId });

        // Fetch created comment
        var sql = @"
            SELECT c.id, c.story_id, c.episode_id, c.parent_comment_id, c.content,
                   c.likes_count, c.replies_count, c.is_pinned, c.is_creator_reply,
                   c.created_at, c.updated_at, c.user_id,
                   u.username, u.display_name, u.avatar_url, u.is_creator as author_is_creator,
                   FALSE as is_liked_by_me,
                   sr.rating as user_rating
            FROM comments c
            JOIN users u ON u.id = c.user_id
            LEFT JOIN story_ratings sr ON sr.user_id = c.user_id AND sr.story_id = c.story_id
            WHERE c.id = @id";

        var comment = await DbHelper.ExecuteReaderFirstAsync(conn, sql, r => MapComment(r, userId),
            new Dictionary<string, object?> { ["id"] = id });

        // Notify story owner (skip if commenter IS the owner, and skip for replies)
        if (comment != null && !req.ParentCommentId.HasValue)
        {
            try
            {
                var storyInfo = await DbHelper.ExecuteReaderFirstAsync(conn,
                    "SELECT creator_id, title FROM stories WHERE id = @sid",
                    r => new { CreatorId = DbHelper.GetGuid(r, "creator_id"), Title = DbHelper.GetString(r, "title") },
                    new Dictionary<string, object?> { ["sid"] = req.StoryId });

                if (storyInfo != null && storyInfo.CreatorId != userId)
                {
                    var preview = req.Content.Length > 40
                        ? req.Content[..40] + "..."
                        : req.Content;
                    var commenterName = comment.AuthorDisplayName?.Length > 0
                        ? comment.AuthorDisplayName
                        : comment.AuthorUsername;
                    await _notifications.CreateNotificationAsync(
                        userId: storyInfo.CreatorId,
                        type: "new_comment",
                        title: "Naya Comment! 💬",
                        message: $"@{comment.AuthorUsername} ne tumhari story '{storyInfo.Title}' par comment kiya: \"{preview}\"",
                        actorId: userId,
                        actionUrl: $"/story/{req.StoryId}/comments");
                }
            }
            catch
            {
                // Notification failure should not break comment creation
            }
        }

        return (true, "Comment add ho gaya", comment);
    }

    public async Task<(bool Success, string Message)> DeleteCommentAsync(Guid userId, Guid commentId)
    {
        using var conn = await _db.CreateConnectionAsync();

        var row = await DbHelper.ExecuteReaderFirstAsync(conn,
            "SELECT user_id, story_id, parent_comment_id FROM comments WHERE id = @id AND deleted_at IS NULL",
            r => new
            {
                OwnerId = DbHelper.GetGuid(r, "user_id"),
                StoryId = DbHelper.GetGuid(r, "story_id"),
                ParentId = r.IsDBNull(r.GetOrdinal("parent_comment_id")) ? (Guid?)null : DbHelper.GetGuid(r, "parent_comment_id")
            },
            new Dictionary<string, object?> { ["id"] = commentId });

        if (row == null) return (false, "Comment nahi mila");
        if (row.OwnerId != userId) return (false, "Sirf apna comment delete kar sakte hain");

        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE comments SET deleted_at = NOW() WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = commentId });

        // Decrement parent replies_count
        if (row.ParentId.HasValue)
        {
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE comments SET replies_count = GREATEST(replies_count - 1, 0) WHERE id = @pid",
                new Dictionary<string, object?> { ["pid"] = row.ParentId.Value });
        }

        // Decrement story total_comments
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE stories SET total_comments = GREATEST(total_comments - 1, 0) WHERE id = @storyId",
            new Dictionary<string, object?> { ["storyId"] = row.StoryId });

        return (true, "Comment delete ho gaya");
    }

    public async Task<(bool Success, string Message)> LikeCommentAsync(Guid userId, Guid commentId)
    {
        using var conn = await _db.CreateConnectionAsync();

        var exists = await DbHelper.ExecuteScalarAsync<bool>(conn,
            "SELECT EXISTS(SELECT 1 FROM comments WHERE id = @id AND deleted_at IS NULL)",
            new Dictionary<string, object?> { ["id"] = commentId });
        if (!exists) return (false, "Comment nahi mila");

        var alreadyLiked = await DbHelper.ExecuteScalarAsync<bool>(conn,
            "SELECT EXISTS(SELECT 1 FROM comment_likes WHERE user_id = @uid AND comment_id = @cid)",
            new Dictionary<string, object?> { ["uid"] = userId, ["cid"] = commentId });
        if (alreadyLiked) return (false, "Pehle se like hai");

        await DbHelper.ExecuteNonQueryAsync(conn,
            "INSERT INTO comment_likes (user_id, comment_id) VALUES (@uid, @cid)",
            new Dictionary<string, object?> { ["uid"] = userId, ["cid"] = commentId });

        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE comments SET likes_count = likes_count + 1 WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = commentId });

        return (true, "Comment like ho gaya");
    }

    public async Task<(bool Success, string Message)> UnlikeCommentAsync(Guid userId, Guid commentId)
    {
        using var conn = await _db.CreateConnectionAsync();

        var rows = await DbHelper.ExecuteNonQueryAsync(conn,
            "DELETE FROM comment_likes WHERE user_id = @uid AND comment_id = @cid",
            new Dictionary<string, object?> { ["uid"] = userId, ["cid"] = commentId });

        if (rows == 0) return (false, "Like nahi tha");

        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE comments SET likes_count = GREATEST(likes_count - 1, 0) WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = commentId });

        return (true, "Like remove ho gaya");
    }

    public async Task<(bool Success, string Message)> ReportCommentAsync(Guid userId, Guid commentId, ReportCommentRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();

        var exists = await DbHelper.ExecuteScalarAsync<bool>(conn,
            "SELECT EXISTS(SELECT 1 FROM comments WHERE id = @id AND deleted_at IS NULL)",
            new Dictionary<string, object?> { ["id"] = commentId });
        if (!exists) return (false, "Comment nahi mila");

        var alreadyReported = await DbHelper.ExecuteScalarAsync<bool>(conn,
            "SELECT EXISTS(SELECT 1 FROM reports WHERE reporter_id = @uid AND entity_type = 'comment' AND entity_id = @eid)",
            new Dictionary<string, object?> { ["uid"] = userId, ["eid"] = commentId });
        if (alreadyReported) return (false, "Pehle se report kar chuke hain");

        await DbHelper.ExecuteNonQueryAsync(conn,
            @"INSERT INTO reports (reporter_id, entity_type, entity_id, reason, custom_reason, severity)
              VALUES (@uid, 'comment', @eid, @reason, @custom, 'medium')",
            new Dictionary<string, object?>
            {
                ["uid"] = userId,
                ["eid"] = commentId,
                ["reason"] = req.Reason,
                ["custom"] = (object?)req.CustomReason ?? DBNull.Value
            });

        return (true, "Report submit ho gaya. Humari team dekh legi.");
    }

    public async Task<(bool Success, string Message)> PinCommentAsync(Guid requesterId, Guid commentId)
    {
        using var conn = await _db.CreateConnectionAsync();

        // Check requester is the story creator
        var isCreator = await DbHelper.ExecuteScalarAsync<bool>(conn,
            @"SELECT EXISTS(
                SELECT 1 FROM comments c
                JOIN stories s ON s.id = c.story_id
                WHERE c.id = @cid AND s.creator_id = @uid AND c.deleted_at IS NULL
              )",
            new Dictionary<string, object?> { ["cid"] = commentId, ["uid"] = requesterId });

        if (!isCreator) return (false, "Sirf story creator comment pin kar sakta hai");

        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE comments SET is_pinned = TRUE WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = commentId });

        return (true, "Comment pin ho gaya");
    }

    public async Task<(bool Success, string Message)> UnpinCommentAsync(Guid requesterId, Guid commentId)
    {
        using var conn = await _db.CreateConnectionAsync();

        var isCreator = await DbHelper.ExecuteScalarAsync<bool>(conn,
            @"SELECT EXISTS(
                SELECT 1 FROM comments c
                JOIN stories s ON s.id = c.story_id
                WHERE c.id = @cid AND s.creator_id = @uid AND c.deleted_at IS NULL
              )",
            new Dictionary<string, object?> { ["cid"] = commentId, ["uid"] = requesterId });

        if (!isCreator) return (false, "Sirf story creator unpin kar sakta hai");

        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE comments SET is_pinned = FALSE WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = commentId });

        return (true, "Comment unpin ho gaya");
    }
}
