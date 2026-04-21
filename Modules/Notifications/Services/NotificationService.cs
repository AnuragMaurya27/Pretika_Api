using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Infrastructure.Database;
using HauntedVoiceUniverse.Infrastructure.Push;
using HauntedVoiceUniverse.Modules.Notifications.Models;
using Npgsql;

namespace HauntedVoiceUniverse.Modules.Notifications.Services;

public interface INotificationService
{
    Task<PagedResult<NotificationResponse>> GetNotificationsAsync(Guid userId, bool? unreadOnly, int page, int pageSize);
    Task<UnreadCountResponse> GetUnreadCountAsync(Guid userId);
    Task<(bool Success, string Message)> MarkAsReadAsync(Guid userId, Guid notificationId);
    Task<(bool Success, string Message)> MarkAllReadAsync(Guid userId);
    Task<(bool Success, string Message)> DeleteNotificationAsync(Guid userId, Guid notificationId);
    Task<NotificationPreferencesResponse> GetPreferencesAsync(Guid userId);
    Task<(bool Success, string Message)> UpdatePreferencesAsync(Guid userId, UpdateNotificationPreferencesRequest req);
    Task<List<AnnouncementResponse>> GetActiveAnnouncementsAsync();

    // Internal use - called by other services
    Task CreateNotificationAsync(Guid userId, string type, string title, string message,
        Guid? actorId = null, string? actionUrl = null, string? imageUrl = null);
}

public class NotificationService : INotificationService
{
    private readonly IDbConnectionFactory _db;
    private readonly IFcmService _fcm;

    public NotificationService(IDbConnectionFactory db, IFcmService fcm)
    {
        _db = db;
        _fcm = fcm;
    }

    private static NotificationResponse MapNotification(NpgsqlDataReader r)
    {
        return new NotificationResponse
        {
            Id = DbHelper.GetGuid(r, "id"),
            NotificationType = DbHelper.GetString(r, "notification_type"),
            Channel = DbHelper.GetString(r, "channel"),
            Title = DbHelper.GetString(r, "title"),
            Message = DbHelper.GetString(r, "message"),
            ImageUrl = DbHelper.GetStringOrNull(r, "image_url"),
            ActionUrl = DbHelper.GetStringOrNull(r, "action_url"),
            IsRead = DbHelper.GetBool(r, "is_read"),
            ReadAt = DbHelper.GetDateTimeOrNull(r, "read_at"),
            CreatedAt = DbHelper.GetDateTime(r, "created_at"),
            ActorId = r.IsDBNull(r.GetOrdinal("actor_id")) ? null : DbHelper.GetGuid(r, "actor_id"),
            ActorUsername = DbHelper.GetStringOrNull(r, "actor_username"),
            ActorAvatarUrl = DbHelper.GetStringOrNull(r, "actor_avatar_url")
        };
    }

    public async Task<PagedResult<NotificationResponse>> GetNotificationsAsync(Guid userId, bool? unreadOnly, int page, int pageSize)
    {
        using var conn = await _db.CreateConnectionAsync();
        pageSize = Math.Min(pageSize, 50);
        int offset = (page - 1) * pageSize;

        var whereClause = "n.user_id = @uid";
        if (unreadOnly == true) whereClause += " AND n.is_read = FALSE";

        var countSql = $"SELECT COUNT(*) FROM notifications n WHERE {whereClause}";
        var total = await DbHelper.ExecuteScalarAsync<long>(conn, countSql,
            new Dictionary<string, object?> { ["uid"] = userId });

        var sql = $@"
            SELECT n.id, n.notification_type, n.channel, n.title, n.message, n.image_url,
                   n.action_url, n.is_read, n.read_at, n.created_at, n.actor_id,
                   u.username as actor_username, u.avatar_url as actor_avatar_url
            FROM notifications n
            LEFT JOIN users u ON u.id = n.actor_id
            WHERE {whereClause}
            ORDER BY n.created_at DESC
            LIMIT @limit OFFSET @offset";

        var items = await DbHelper.ExecuteReaderAsync(conn, sql, MapNotification,
            new Dictionary<string, object?> { ["uid"] = userId, ["limit"] = pageSize, ["offset"] = offset });

        return PagedResult<NotificationResponse>.Create(items, (int)total, page, pageSize);
    }

    public async Task<UnreadCountResponse> GetUnreadCountAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        var count = await DbHelper.ExecuteScalarAsync<long>(conn,
            "SELECT COUNT(*) FROM notifications WHERE user_id = @uid AND is_read = FALSE",
            new Dictionary<string, object?> { ["uid"] = userId });
        return new UnreadCountResponse { UnreadCount = (int)count };
    }

    public async Task<(bool Success, string Message)> MarkAsReadAsync(Guid userId, Guid notificationId)
    {
        using var conn = await _db.CreateConnectionAsync();
        var rows = await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE notifications SET is_read = TRUE, read_at = NOW() WHERE id = @id AND user_id = @uid AND is_read = FALSE",
            new Dictionary<string, object?> { ["id"] = notificationId, ["uid"] = userId });

        return rows > 0 ? (true, "Read mark ho gaya") : (false, "Notification nahi mili ya pehle se read hai");
    }

    public async Task<(bool Success, string Message)> MarkAllReadAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE notifications SET is_read = TRUE, read_at = NOW() WHERE user_id = @uid AND is_read = FALSE",
            new Dictionary<string, object?> { ["uid"] = userId });
        return (true, "Saari notifications read mark ho gayin");
    }

    public async Task<(bool Success, string Message)> DeleteNotificationAsync(Guid userId, Guid notificationId)
    {
        using var conn = await _db.CreateConnectionAsync();
        var rows = await DbHelper.ExecuteNonQueryAsync(conn,
            "DELETE FROM notifications WHERE id = @id AND user_id = @uid",
            new Dictionary<string, object?> { ["id"] = notificationId, ["uid"] = userId });
        return rows > 0 ? (true, "Notification delete ho gayi") : (false, "Notification nahi mili");
    }

    public async Task<NotificationPreferencesResponse> GetPreferencesAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();

        // Ensure preferences row exists
        await DbHelper.ExecuteNonQueryAsync(conn,
            "INSERT INTO notification_preferences (user_id) VALUES (@uid) ON CONFLICT (user_id) DO NOTHING",
            new Dictionary<string, object?> { ["uid"] = userId });

        return await DbHelper.ExecuteReaderFirstAsync(conn,
            "SELECT * FROM notification_preferences WHERE user_id = @uid",
            r => new NotificationPreferencesResponse
            {
                EmailNewFollower = DbHelper.GetBool(r, "email_new_follower"),
                EmailNewComment = DbHelper.GetBool(r, "email_new_comment"),
                EmailNewEpisode = DbHelper.GetBool(r, "email_new_episode"),
                EmailCoinReceived = DbHelper.GetBool(r, "email_coin_received"),
                EmailAnnouncements = DbHelper.GetBool(r, "email_announcements"),
                PushNewFollower = DbHelper.GetBool(r, "push_new_follower"),
                PushNewComment = DbHelper.GetBool(r, "push_new_comment"),
                PushNewEpisode = DbHelper.GetBool(r, "push_new_episode"),
                PushCoinReceived = DbHelper.GetBool(r, "push_coin_received"),
                PushChatMessage = DbHelper.GetBool(r, "push_chat_message"),
                PushAnnouncements = DbHelper.GetBool(r, "push_announcements"),
                QuietHoursStart = r.IsDBNull(r.GetOrdinal("quiet_hours_start")) ? null : r.GetFieldValue<TimeOnly>(r.GetOrdinal("quiet_hours_start")).ToString("HH:mm"),
                QuietHoursEnd = r.IsDBNull(r.GetOrdinal("quiet_hours_end")) ? null : r.GetFieldValue<TimeOnly>(r.GetOrdinal("quiet_hours_end")).ToString("HH:mm")
            },
            new Dictionary<string, object?> { ["uid"] = userId }) ?? new NotificationPreferencesResponse();
    }

    public async Task<(bool Success, string Message)> UpdatePreferencesAsync(Guid userId, UpdateNotificationPreferencesRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();

        // Ensure row exists
        await DbHelper.ExecuteNonQueryAsync(conn,
            "INSERT INTO notification_preferences (user_id) VALUES (@uid) ON CONFLICT (user_id) DO NOTHING",
            new Dictionary<string, object?> { ["uid"] = userId });

        var setParts = new List<string> { "updated_at = NOW()" };
        var parameters = new Dictionary<string, object?> { ["uid"] = userId };

        void AddIf(string col, string param, object? val)
        {
            if (val != null) { setParts.Add($"{col} = @{param}"); parameters[param] = val; }
        }

        AddIf("email_new_follower", "enf", req.EmailNewFollower);
        AddIf("email_new_comment", "enc", req.EmailNewComment);
        AddIf("email_new_episode", "ene", req.EmailNewEpisode);
        AddIf("email_coin_received", "ecr", req.EmailCoinReceived);
        AddIf("email_announcements", "ea", req.EmailAnnouncements);
        AddIf("push_new_follower", "pnf", req.PushNewFollower);
        AddIf("push_new_comment", "pnc", req.PushNewComment);
        AddIf("push_new_episode", "pne", req.PushNewEpisode);
        AddIf("push_coin_received", "pcr", req.PushCoinReceived);
        AddIf("push_chat_message", "pcm", req.PushChatMessage);
        AddIf("push_announcements", "pa", req.PushAnnouncements);

        if (!string.IsNullOrEmpty(req.QuietHoursStart))
        { setParts.Add("quiet_hours_start = @qhs::time"); parameters["qhs"] = req.QuietHoursStart; }
        if (!string.IsNullOrEmpty(req.QuietHoursEnd))
        { setParts.Add("quiet_hours_end = @qhe::time"); parameters["qhe"] = req.QuietHoursEnd; }

        if (setParts.Count == 1) return (false, "Kuch update nahi kiya");

        await DbHelper.ExecuteNonQueryAsync(conn,
            $"UPDATE notification_preferences SET {string.Join(", ", setParts)} WHERE user_id = @uid",
            parameters);

        return (true, "Preferences update ho gayi");
    }

    public async Task<List<AnnouncementResponse>> GetActiveAnnouncementsAsync()
    {
        using var conn = await _db.CreateConnectionAsync();
        return await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT id, title, message, banner_url, display_type, priority, starts_at, ends_at
              FROM announcements
              WHERE is_active = TRUE AND starts_at <= NOW() AND ends_at >= NOW()
              ORDER BY priority DESC, starts_at DESC",
            r => new AnnouncementResponse
            {
                Id = DbHelper.GetGuid(r, "id"),
                Title = DbHelper.GetString(r, "title"),
                Message = DbHelper.GetString(r, "message"),
                BannerUrl = DbHelper.GetStringOrNull(r, "banner_url"),
                DisplayType = DbHelper.GetStringOrNull(r, "display_type"),
                Priority = DbHelper.GetInt(r, "priority"),
                StartsAt = DbHelper.GetDateTime(r, "starts_at"),
                EndsAt = DbHelper.GetDateTime(r, "ends_at")
            });
    }

    public async Task CreateNotificationAsync(Guid userId, string type, string title, string message,
        Guid? actorId = null, string? actionUrl = null, string? imageUrl = null)
    {
        try
        {
            using var conn = await _db.CreateConnectionAsync();
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO notifications (user_id, notification_type, title, message, actor_id, action_url, image_url, sent_at)
                  VALUES (@uid, @type::notification_type, @title, @message, @actorId, @actionUrl, @imageUrl, NOW())",
                new Dictionary<string, object?>
                {
                    ["uid"] = userId,
                    ["type"] = type,
                    ["title"] = title,
                    ["message"] = message,
                    ["actorId"] = (object?)actorId ?? DBNull.Value,
                    ["actionUrl"] = (object?)actionUrl ?? DBNull.Value,
                    ["imageUrl"] = (object?)imageUrl ?? DBNull.Value
                });

            // Fire FCM push (non-blocking, failure won't break main flow)
            var fcmData = new Dictionary<string, string>
            {
                ["notification_type"] = type
            };
            if (!string.IsNullOrEmpty(actionUrl)) fcmData["action_url"] = actionUrl;
            if (!string.IsNullOrEmpty(imageUrl)) fcmData["image_url"] = imageUrl;
            if (actorId.HasValue) fcmData["actor_id"] = actorId.Value.ToString();

            _ = _fcm.SendToUserAsync(userId, title, message, type, fcmData);
        }
        catch
        {
            // Notification failure should not break main flow
        }
    }
}
