using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Infrastructure.Database;
using HauntedVoiceUniverse.Modules.Comments.Models;
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

    public CommentService(IDbConnectionFactory db)
    {
        _db = db;
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
            IsMyComment = viewerId.HasValue && DbHelper.GetGuid(r, "user_id") == viewerId.Value
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
                   CASE WHEN cl.id IS NOT NULL THEN TRUE ELSE FALSE END as is_liked_by_me
            FROM comments c
            JOIN users u ON u.id = c.user_id
            LEFT JOIN comment_likes cl ON cl.comment_id = c.id AND cl.user_id = @viewerId
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
                   CASE WHEN cl.id IS NOT NULL THEN TRUE ELSE FALSE END as is_liked_by_me
            FROM comments c
            JOIN users u ON u.id = c.user_id
            LEFT JOIN comment_likes cl ON cl.comment_id = c.id AND cl.user_id = @viewerId
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

        // Fetch created comment
        var sql = @"
            SELECT c.id, c.story_id, c.episode_id, c.parent_comment_id, c.content,
                   c.likes_count, c.replies_count, c.is_pinned, c.is_creator_reply,
                   c.created_at, c.updated_at, c.user_id,
                   u.username, u.display_name, u.avatar_url, u.is_creator as author_is_creator,
                   FALSE as is_liked_by_me
            FROM comments c
            JOIN users u ON u.id = c.user_id
            WHERE c.id = @id";

        var comment = await DbHelper.ExecuteReaderFirstAsync(conn, sql, r => MapComment(r, userId),
            new Dictionary<string, object?> { ["id"] = id });

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
