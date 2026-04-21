using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Infrastructure.Database;
using HauntedVoiceUniverse.Infrastructure.Push;
using HauntedVoiceUniverse.Modules.Chat.Hubs;
using HauntedVoiceUniverse.Modules.Chat.Models;
using Microsoft.AspNetCore.SignalR;
using Npgsql;

namespace HauntedVoiceUniverse.Modules.Chat.Services;

public interface IChatService
{
    Task<List<ChatRoomResponse>> GetPublicRoomsAsync(Guid? viewerId);
    Task<List<ChatRoomResponse>> GetMyPrivateChatsAsync(Guid userId);
    Task<(bool Success, string Message, ChatRoomResponse? Data)> StartPrivateChatAsync(Guid userId, StartPrivateChatRequest req);
    Task<PagedResult<ChatMessageResponse>> GetMessagesAsync(Guid userId, Guid roomId, int page, int pageSize);
    Task<(bool Success, string Message, ChatMessageResponse? Data)> SendMessageAsync(Guid userId, Guid roomId, SendMessageRequest req);
    Task<(bool Success, string Message)> DeleteMessageAsync(Guid userId, Guid messageId);
    // BUG#M7-7 FIX: Admin-level delete — bypasses sender_id check, broadcasts deletion to all clients.
    Task<(bool Success, string Message)> AdminDeleteMessageAsync(Guid adminId, Guid messageId);
    Task<(bool Success, string Message)> JoinPublicRoomAsync(Guid userId, Guid roomId);
    Task<(bool Success, string Message)> LeavePublicRoomAsync(Guid userId, Guid roomId);
    Task<List<StickerPackResponse>> GetStickerPacksAsync(Guid? userId);
    Task<(bool Success, string Message)> BuyStickerPackAsync(Guid userId, Guid packId);
    Task<(bool Success, string Message)> ReportMessageAsync(Guid userId, Guid messageId, ReportMessageRequest req);
}

public class ChatService : IChatService
{
    private readonly IDbConnectionFactory _db;
    private readonly IConfiguration _config;
    private readonly IHubContext<ChatHub> _hub;
    private readonly IFcmService _fcm;

    public ChatService(IDbConnectionFactory db, IConfiguration config, IHubContext<ChatHub> hub, IFcmService fcm)
    {
        _db = db;
        _config = config;
        _hub = hub;
        _fcm = fcm;
    }

    private static ChatMessageResponse MapMessage(NpgsqlDataReader r)
    {
        return new ChatMessageResponse
        {
            Id = DbHelper.GetGuid(r, "id"),
            RoomId = DbHelper.GetGuid(r, "room_id"),
            SenderId = DbHelper.GetGuid(r, "sender_id"),
            SenderUsername = DbHelper.GetString(r, "sender_username"),
            SenderDisplayName = DbHelper.GetStringOrNull(r, "sender_display_name"),
            SenderAvatarUrl = DbHelper.GetStringOrNull(r, "sender_avatar_url"),
            MessageType = DbHelper.GetString(r, "message_type"),
            Content = DbHelper.GetBool(r, "is_deleted") ? null : DbHelper.GetStringOrNull(r, "content"),
            ImageUrl = DbHelper.GetBool(r, "is_deleted") ? null : DbHelper.GetStringOrNull(r, "image_url"),
            StickerId = DbHelper.GetStringOrNull(r, "sticker_id"),
            IsSuperChat = DbHelper.GetBool(r, "is_super_chat"),
            SuperChatCoins = DbHelper.GetInt(r, "super_chat_coins"),
            SuperChatHighlightColor = DbHelper.GetStringOrNull(r, "super_chat_highlight_color"),
            IsDeleted = DbHelper.GetBool(r, "is_deleted"),
            IsSystemMessage = DbHelper.GetBool(r, "is_system_message"),
            CreatedAt = DbHelper.GetDateTime(r, "created_at")
        };
    }

    public async Task<List<ChatRoomResponse>> GetPublicRoomsAsync(Guid? viewerId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        var rooms = await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT cr.id, cr.room_type, cr.name, cr.name_hi, cr.description, cr.icon_url,
                     cr.member_count, cr.is_active, cr.is_restricted, cr.last_message_at,
                     CASE WHEN crm.id IS NOT NULL THEN TRUE ELSE FALSE END as is_member
              FROM chat_rooms cr
              LEFT JOIN chat_room_members crm ON crm.room_id = cr.id AND crm.user_id = @viewerId
              WHERE cr.room_type = 'public' AND cr.is_active = TRUE
              ORDER BY cr.member_count DESC",
            r => new ChatRoomResponse
            {
                Id = DbHelper.GetGuid(r, "id"),
                RoomType = DbHelper.GetString(r, "room_type"),
                Name = DbHelper.GetStringOrNull(r, "name"),
                NameHi = DbHelper.GetStringOrNull(r, "name_hi"),
                Description = DbHelper.GetStringOrNull(r, "description"),
                IconUrl = DbHelper.GetStringOrNull(r, "icon_url"),
                MemberCount = DbHelper.GetInt(r, "member_count"),
                IsActive = DbHelper.GetBool(r, "is_active"),
                IsRestricted = DbHelper.GetBool(r, "is_restricted"),
                LastMessageAt = DbHelper.GetDateTimeOrNull(r, "last_message_at"),
                IsMember = DbHelper.GetBool(r, "is_member")
            },
            new Dictionary<string, object?> { ["viewerId"] = (object?)viewerId ?? DBNull.Value });

        return rooms;
    }

    public async Task<List<ChatRoomResponse>> GetMyPrivateChatsAsync(Guid userId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        return await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT cr.id, cr.room_type, cr.last_message_at,
                     CASE WHEN cr.user1_id = @uid THEN cr.user2_id ELSE cr.user1_id END as other_user_id,
                     u.username as other_username, u.avatar_url as other_avatar_url,
                     lm.id          as lm_id,
                     lm.room_id     as lm_room_id,
                     lm.sender_id   as lm_sender_id,
                     lm.message_type as lm_message_type,
                     lm.content     as lm_content,
                     lm.image_url   as lm_image_url,
                     lm.sticker_id  as lm_sticker_id,
                     lm.is_super_chat       as lm_is_super_chat,
                     lm.super_chat_coins    as lm_super_chat_coins,
                     lm.is_deleted          as lm_is_deleted,
                     lm.is_system_message   as lm_is_system_message,
                     lm.created_at          as lm_created_at
              FROM chat_rooms cr
              JOIN users u ON u.id = CASE WHEN cr.user1_id = @uid THEN cr.user2_id ELSE cr.user1_id END
              LEFT JOIN LATERAL (
                  SELECT cm.id, cm.room_id, cm.sender_id, cm.message_type, cm.content,
                         cm.image_url, cm.sticker_id, cm.is_super_chat, cm.super_chat_coins,
                         cm.is_deleted, cm.is_system_message, cm.created_at
                  FROM chat_messages cm
                  WHERE cm.room_id = cr.id AND cm.is_deleted = FALSE
                  ORDER BY cm.created_at DESC
                  LIMIT 1
              ) lm ON TRUE
              WHERE cr.room_type = 'private'
                AND (cr.user1_id = @uid OR cr.user2_id = @uid)
                AND cr.is_active = TRUE
              ORDER BY cr.last_message_at DESC NULLS LAST",
            r =>
            {
                var hasLastMsg = !r.IsDBNull(r.GetOrdinal("lm_id"));
                return new ChatRoomResponse
                {
                    Id = DbHelper.GetGuid(r, "id"),
                    RoomType = DbHelper.GetString(r, "room_type"),
                    LastMessageAt = DbHelper.GetDateTimeOrNull(r, "last_message_at"),
                    OtherUserId = DbHelper.GetGuid(r, "other_user_id"),
                    OtherUsername = DbHelper.GetString(r, "other_username"),
                    OtherAvatarUrl = DbHelper.GetStringOrNull(r, "other_avatar_url"),
                    IsMember = true,
                    IsActive = true,
                    LastMessage = hasLastMsg ? new ChatMessageResponse
                    {
                        Id = r.GetGuid(r.GetOrdinal("lm_id")),
                        RoomId = r.GetGuid(r.GetOrdinal("lm_room_id")),
                        SenderId = r.GetGuid(r.GetOrdinal("lm_sender_id")),
                        MessageType = DbHelper.GetString(r, "lm_message_type"),
                        Content = DbHelper.GetStringOrNull(r, "lm_content"),
                        ImageUrl = DbHelper.GetStringOrNull(r, "lm_image_url"),
                        StickerId = DbHelper.GetStringOrNull(r, "lm_sticker_id"),
                        IsSuperChat = DbHelper.GetBool(r, "lm_is_super_chat"),
                        SuperChatCoins = DbHelper.GetInt(r, "lm_super_chat_coins"),
                        IsDeleted = DbHelper.GetBool(r, "lm_is_deleted"),
                        IsSystemMessage = DbHelper.GetBool(r, "lm_is_system_message"),
                        CreatedAt = DbHelper.GetDateTime(r, "lm_created_at")
                    } : null
                };
            },
            new Dictionary<string, object?> { ["uid"] = userId });
    }

    public async Task<(bool Success, string Message, ChatRoomResponse? Data)> StartPrivateChatAsync(Guid userId, StartPrivateChatRequest req)
    {
        if (req.TargetUserId == userId) return (false, "Apne aap se chat nahi kar sakte", null);

        await using var conn = await _db.CreateConnectionAsync();

        // Check mutual follow
        var mutualFollow = await DbHelper.ExecuteScalarAsync<bool>(conn,
            @"SELECT EXISTS(
                SELECT 1 FROM follows f1
                WHERE f1.follower_id = @uid AND f1.following_id = @tid
              ) AND EXISTS(
                SELECT 1 FROM follows f2
                WHERE f2.follower_id = @tid AND f2.following_id = @uid
              )",
            new Dictionary<string, object?> { ["uid"] = userId, ["tid"] = req.TargetUserId });

        if (!mutualFollow) return (false, "Private chat ke liye dono ko ek doosre ko follow karna padega", null);

        // Check if room already exists (order user1 < user2 for consistency)
        var u1 = userId < req.TargetUserId ? userId : req.TargetUserId;
        var u2 = userId < req.TargetUserId ? req.TargetUserId : userId;

        var existingRoom = await DbHelper.ExecuteReaderFirstAsync(conn,
            "SELECT id FROM chat_rooms WHERE room_type = 'private' AND user1_id = @u1 AND user2_id = @u2",
            r => DbHelper.GetGuid(r, "id"),
            new Dictionary<string, object?> { ["u1"] = u1, ["u2"] = u2 });

        Guid roomId;
        if (existingRoom != Guid.Empty && existingRoom != default)
        {
            roomId = existingRoom;
        }
        else
        {
            roomId = Guid.NewGuid();
            await DbHelper.ExecuteNonQueryAsync(conn,
                "INSERT INTO chat_rooms (id, room_type, user1_id, user2_id) VALUES (@id, 'private', @u1, @u2)",
                new Dictionary<string, object?> { ["id"] = roomId, ["u1"] = u1, ["u2"] = u2 });
        }

        var targetUser = await DbHelper.ExecuteReaderFirstAsync(conn,
            "SELECT id, username, avatar_url FROM users WHERE id = @uid",
            r => new { Id = DbHelper.GetGuid(r, "id"), Username = DbHelper.GetString(r, "username"), AvatarUrl = DbHelper.GetStringOrNull(r, "avatar_url") },
            new Dictionary<string, object?> { ["uid"] = req.TargetUserId });

        return (true, "Chat room ready hai", new ChatRoomResponse
        {
            Id = roomId,
            RoomType = "private",
            OtherUserId = req.TargetUserId,
            OtherUsername = targetUser?.Username ?? "",
            OtherAvatarUrl = targetUser?.AvatarUrl,
            IsMember = true,
            IsActive = true
        });
    }

    public async Task<PagedResult<ChatMessageResponse>> GetMessagesAsync(Guid userId, Guid roomId, int page, int pageSize)
    {
        await using var conn = await _db.CreateConnectionAsync();

        // Verify access
        var hasAccess = await DbHelper.ExecuteScalarAsync<bool>(conn,
            @"SELECT EXISTS(
                SELECT 1 FROM chat_rooms cr
                WHERE cr.id = @rid AND cr.is_active = TRUE
                  AND (cr.room_type = 'public'
                    OR (cr.room_type = 'private' AND (cr.user1_id = @uid OR cr.user2_id = @uid)))
              )",
            new Dictionary<string, object?> { ["rid"] = roomId, ["uid"] = userId });

        if (!hasAccess) return PagedResult<ChatMessageResponse>.Create(new(), 0, page, pageSize);

        pageSize = Math.Min(pageSize, 100);
        int offset = (page - 1) * pageSize;

        var total = await DbHelper.ExecuteScalarAsync<long>(conn,
            "SELECT COUNT(*) FROM chat_messages WHERE room_id = @rid",
            new Dictionary<string, object?> { ["rid"] = roomId });

        // BUG#M7-6 FIX: Changed INNER JOIN → LEFT JOIN so messages from deleted accounts
        // are not silently dropped. COALESCE returns "[Deleted User]" for missing senders.
        var messages = await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT cm.*,
                     COALESCE(u.username, '[deleted]') as sender_username,
                     u.display_name as sender_display_name,
                     u.avatar_url as sender_avatar_url
              FROM chat_messages cm
              LEFT JOIN users u ON u.id = cm.sender_id
              WHERE cm.room_id = @rid
              ORDER BY cm.created_at DESC
              LIMIT @limit OFFSET @offset",
            MapMessage,
            new Dictionary<string, object?> { ["rid"] = roomId, ["limit"] = pageSize, ["offset"] = offset });

        messages.Reverse(); // Chronological order for display
        return PagedResult<ChatMessageResponse>.Create(messages, (int)total, page, pageSize);
    }

    public async Task<(bool Success, string Message, ChatMessageResponse? Data)> SendMessageAsync(Guid userId, Guid roomId, SendMessageRequest req)
    {
        await using var conn = await _db.CreateConnectionAsync();

        // Validate room access
        var room = await DbHelper.ExecuteReaderFirstAsync(conn,
            @"SELECT id, room_type, is_restricted, user1_id, user2_id FROM chat_rooms
              WHERE id = @rid AND is_active = TRUE",
            r => new
            {
                Id = DbHelper.GetGuid(r, "id"),
                RoomType = DbHelper.GetString(r, "room_type"),
                IsRestricted = DbHelper.GetBool(r, "is_restricted"),
                User1 = r.IsDBNull(r.GetOrdinal("user1_id")) ? (Guid?)null : DbHelper.GetGuid(r, "user1_id"),
                User2 = r.IsDBNull(r.GetOrdinal("user2_id")) ? (Guid?)null : DbHelper.GetGuid(r, "user2_id")
            },
            new Dictionary<string, object?> { ["rid"] = roomId });

        if (room == null) return (false, "Room nahi mila", null);
        if (room.IsRestricted) return (false, "Yeh room restricted hai", null);

        // Private chat validation
        if (room.RoomType == "private")
        {
            if (room.User1 != userId && room.User2 != userId) return (false, "Is room mein access nahi hai", null);

            var otherUserId = room.User1 == userId ? room.User2 : room.User1;

            // BUG#M7-1 FIX: Block check — if either party blocked the other, messages are blocked.
            if (otherUserId.HasValue)
            {
                var isBlocked = await DbHelper.ExecuteScalarAsync<bool>(conn,
                    @"SELECT EXISTS(
                        SELECT 1 FROM blocks
                        WHERE (blocker_id = @uid AND blocked_id = @other)
                           OR (blocker_id = @other AND blocked_id = @uid)
                      )",
                    new Dictionary<string, object?> { ["uid"] = userId, ["other"] = otherUserId.Value });
                if (isBlocked) return (false, "Is user ke saath message nahi kar sakte", null);
            }

            // BUG#M7-2 FIX: Re-validate mutual follow — if A or B unfollows, new messages are blocked.
            // Existing history remains readable (GetMessagesAsync has no such check), only new sends blocked.
            if (otherUserId.HasValue)
            {
                var stillMutual = await DbHelper.ExecuteScalarAsync<bool>(conn,
                    @"SELECT EXISTS(SELECT 1 FROM follows WHERE follower_id = @uid AND following_id = @other)
                        AND EXISTS(SELECT 1 FROM follows WHERE follower_id = @other AND following_id = @uid)",
                    new Dictionary<string, object?> { ["uid"] = userId, ["other"] = otherUserId.Value });
                if (!stillMutual) return (false, "Private chat ke liye mutual follow zaroori hai", null);
            }
        }

        // BUG#M7-3 FIX: Idempotency check — prevent duplicate messages on retry
        if (!string.IsNullOrEmpty(req.IdempotencyKey))
        {
            var existingMsg = await DbHelper.ExecuteReaderFirstAsync(conn,
                @"SELECT cm.*, u.username as sender_username, u.display_name as sender_display_name, u.avatar_url as sender_avatar_url
                  FROM chat_messages cm
                  JOIN users u ON u.id = cm.sender_id
                  WHERE cm.idempotency_key = @key AND cm.sender_id = @uid AND cm.room_id = @rid",
                MapMessage,
                new Dictionary<string, object?> { ["key"] = req.IdempotencyKey, ["uid"] = userId, ["rid"] = roomId });
            if (existingMsg != null) return (true, "Message already sent", existingMsg);
        }

        // BUG#6 FIX: Reject super_chat with 0 coins
        if (req.MessageType == "super_chat" && req.SuperChatCoins <= 0)
            return (false, "Super chat ke liye minimum 10 coins chahiye", null);

        // BUG#3 + BUG#4 FIX: Wrap ALL super chat coin ops + message insert in ONE transaction
        Guid? txId = null;
        string? highlightColor = null;

        using var dbTx = await conn.BeginTransactionAsync();
        try
        {
            if (req.MessageType == "super_chat" && req.SuperChatCoins > 0)
            {
                // BUG#4 FIX: FOR UPDATE prevents concurrent over-spend (race condition)
                var wallet = await DbHelper.ExecuteReaderFirstAsync(conn,
                    "SELECT coin_balance, is_frozen FROM wallets WHERE user_id = @uid FOR UPDATE",
                    r => new { Balance = DbHelper.GetLong(r, "coin_balance"), IsFrozen = DbHelper.GetBool(r, "is_frozen") },
                    new Dictionary<string, object?> { ["uid"] = userId });

                if (wallet == null || wallet.IsFrozen)
                {
                    await dbTx.RollbackAsync();
                    return (false, "Wallet nahi mila ya frozen hai", null);
                }
                if (wallet.Balance < req.SuperChatCoins)
                {
                    await dbTx.RollbackAsync();
                    return (false, "Insufficient coins", null);
                }

                txId = Guid.NewGuid();
                await DbHelper.ExecuteNonQueryAsync(conn,
                    @"INSERT INTO coin_transactions
                        (id, sender_id, transaction_type, status, amount,
                         reference_type, reference_id, description, completed_at)
                      VALUES (@id, @uid, 'super_chat', 'completed', @amount,
                              'chat_room', @rid, 'Super Chat', NOW())",
                    new Dictionary<string, object?> { ["id"] = txId, ["uid"] = userId, ["amount"] = req.SuperChatCoins, ["rid"] = roomId },
                    dbTx);

                await DbHelper.ExecuteNonQueryAsync(conn,
                    "UPDATE wallets SET coin_balance = coin_balance - @amount, total_spent = total_spent + @amount WHERE user_id = @uid",
                    new Dictionary<string, object?> { ["uid"] = userId, ["amount"] = req.SuperChatCoins },
                    dbTx);

                highlightColor = req.SuperChatHighlightColor ?? "#FF4444";
            }

            var msgId = Guid.NewGuid();
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO chat_messages
                    (id, room_id, sender_id, message_type, content, image_url, sticker_id,
                     is_super_chat, super_chat_coins, super_chat_highlight_color, transaction_id, idempotency_key)
                  VALUES
                    (@id, @rid, @uid, @type::message_type, @content, @imgUrl, @stickerId,
                     @isSuperChat, @superCoins, @highlightColor, @txId, @ikey)",
                new Dictionary<string, object?>
                {
                    ["id"]             = msgId,
                    ["rid"]            = roomId,
                    ["uid"]            = userId,
                    ["type"]           = req.MessageType,
                    ["content"]        = (object?)req.Content ?? DBNull.Value,
                    ["imgUrl"]         = (object?)req.ImageUrl ?? DBNull.Value,
                    ["stickerId"]      = (object?)req.StickerId ?? DBNull.Value,
                    ["isSuperChat"]    = req.MessageType == "super_chat",
                    ["superCoins"]     = req.SuperChatCoins,
                    ["highlightColor"] = (object?)highlightColor ?? DBNull.Value,
                    ["txId"]           = (object?)txId ?? DBNull.Value,
                    ["ikey"]           = (object?)req.IdempotencyKey ?? DBNull.Value
                }, dbTx);

            // Update room last_message_at
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE chat_rooms SET last_message_at = NOW() WHERE id = @id",
                new Dictionary<string, object?> { ["id"] = roomId }, dbTx);

            await dbTx.CommitAsync();

            // Fetch sender info for broadcast (after commit — outside locked tx)
            string senderUsername = "", senderDisplayName = "", senderAvatarUrl = "";
            using var uCmd = new NpgsqlCommand(
                "SELECT username, display_name, avatar_url FROM users WHERE id = @uid", conn);
            uCmd.Parameters.AddWithValue("@uid", userId);
            using (var uRdr = await uCmd.ExecuteReaderAsync())
            {
                if (await uRdr.ReadAsync())
                {
                    senderUsername    = uRdr.IsDBNull(0) ? "" : uRdr.GetString(0);
                    senderDisplayName = uRdr.IsDBNull(1) ? "" : uRdr.GetString(1);
                    senderAvatarUrl   = uRdr.IsDBNull(2) ? "" : uRdr.GetString(2);
                }
            }

            var msgResponse = new ChatMessageResponse
            {
                Id                      = msgId,
                RoomId                  = roomId,
                SenderId                = userId,
                SenderUsername          = senderUsername,
                SenderDisplayName       = senderDisplayName,
                SenderAvatarUrl         = senderAvatarUrl,
                MessageType             = req.MessageType,
                Content                 = req.Content,
                ImageUrl                = req.ImageUrl,
                StickerId               = req.StickerId,
                IsSuperChat             = req.MessageType == "super_chat",
                SuperChatCoins          = req.SuperChatCoins,
                SuperChatHighlightColor = highlightColor,
                CreatedAt               = DateTime.UtcNow
            };

            // Broadcast to all connected clients in this room via SignalR
            _ = _hub.Clients.Group(roomId.ToString()).SendAsync("NewMessage", msgResponse);

            // Send FCM push to recipient for private chats (non-blocking)
            _ = SendPrivateChatPushAsync(conn, roomId, userId, senderUsername, senderDisplayName, req);

            return (true, "Message bheja", msgResponse);
        }
        catch
        {
            // BUG#3 FIX: if message insert fails, coin deduction is also rolled back
            await dbTx.RollbackAsync();
            return (false, "Message send karne mein error aaya. Coins deduct nahi hue.", null);
        }
    }

    private async Task SendPrivateChatPushAsync(
        Npgsql.NpgsqlConnection conn, Guid roomId, Guid senderId,
        string senderUsername, string senderDisplayName, SendMessageRequest req)
    {
        try
        {
            // Only send push for private rooms
            var roomType = await DbHelper.ExecuteScalarAsync<string?>(conn,
                "SELECT room_type FROM chat_rooms WHERE id = @id",
                new Dictionary<string, object?> { ["id"] = roomId });
            if (roomType != "private") return;

            // Get the other user in the private room
            var recipientId = await DbHelper.ExecuteScalarAsync<Guid?>(conn,
                "SELECT CASE WHEN user1_id = @uid THEN user2_id ELSE user1_id END FROM chat_rooms WHERE id = @rid",
                new Dictionary<string, object?> { ["uid"] = senderId, ["rid"] = roomId });
            if (recipientId == null) return;

            var displayName = string.IsNullOrEmpty(senderDisplayName) ? senderUsername : senderDisplayName;
            var body = req.MessageType switch
            {
                "sticker" => "Sticker bheja",
                "image"   => "Photo bheja",
                _ => req.Content ?? ""
            };
            if (body.Length > 100) body = body[..100] + "...";

            _ = _fcm.SendToUserAsync(
                recipientId.Value,
                displayName,
                body,
                "message",
                new Dictionary<string, string>
                {
                    ["room_id"]    = roomId.ToString(),
                    ["action_url"] = $"/chat/room/{roomId}",
                    ["sender_username"] = senderUsername
                });
        }
        catch { }
    }

    public async Task<(bool Success, string Message)> DeleteMessageAsync(Guid userId, Guid messageId)
    {
        await using var conn = await _db.CreateConnectionAsync();
        var rows = await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE chat_messages SET is_deleted = TRUE, deleted_by = @uid, deleted_at = NOW() WHERE id = @id AND sender_id = @uid AND is_deleted = FALSE",
            new Dictionary<string, object?> { ["id"] = messageId, ["uid"] = userId });

        if (rows > 0)
        {
            // Broadcast deletion event so all connected clients remove/redact the message
            var roomId = await DbHelper.ExecuteScalarAsync<string?>(conn,
                "SELECT room_id::text FROM chat_messages WHERE id = @id",
                new Dictionary<string, object?> { ["id"] = messageId });
            if (!string.IsNullOrEmpty(roomId))
                _ = _hub.Clients.Group(roomId).SendAsync("MessageDeleted", new { MessageId = messageId, RoomId = roomId });
        }

        return rows > 0 ? (true, "Message delete ho gaya") : (false, "Message nahi mila ya pehle se delete hai");
    }

    // BUG#M7-7 FIX: Admin-level message delete — no sender_id restriction.
    // Only super_admin and moderator roles should call this (enforced at controller level).
    // Broadcasts MessageDeleted event to all clients in the room so the message disappears
    // for everyone, not just the admin's view.
    public async Task<(bool Success, string Message)> AdminDeleteMessageAsync(Guid adminId, Guid messageId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        // Fetch room_id before deleting (needed for broadcast)
        var roomIdStr = await DbHelper.ExecuteScalarAsync<string?>(conn,
            "SELECT room_id::text FROM chat_messages WHERE id = @id AND is_deleted = FALSE",
            new Dictionary<string, object?> { ["id"] = messageId });

        if (string.IsNullOrEmpty(roomIdStr)) return (false, "Message nahi mila ya pehle se delete hai");

        var rows = await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE chat_messages SET is_deleted = TRUE, deleted_by = @admin, deleted_at = NOW() WHERE id = @id AND is_deleted = FALSE",
            new Dictionary<string, object?> { ["id"] = messageId, ["admin"] = adminId });

        if (rows > 0)
            _ = _hub.Clients.Group(roomIdStr).SendAsync("MessageDeleted", new { MessageId = messageId, RoomId = roomIdStr });

        return rows > 0 ? (true, "Message admin ne delete kar diya") : (false, "Delete nahi hua");
    }

    public async Task<(bool Success, string Message)> JoinPublicRoomAsync(Guid userId, Guid roomId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        var room = await DbHelper.ExecuteScalarAsync<bool>(conn,
            "SELECT EXISTS(SELECT 1 FROM chat_rooms WHERE id = @id AND room_type = 'public' AND is_active = TRUE)",
            new Dictionary<string, object?> { ["id"] = roomId });
        if (!room) return (false, "Room nahi mila");

        var alreadyMember = await DbHelper.ExecuteScalarAsync<bool>(conn,
            "SELECT EXISTS(SELECT 1 FROM chat_room_members WHERE room_id = @rid AND user_id = @uid)",
            new Dictionary<string, object?> { ["rid"] = roomId, ["uid"] = userId });
        if (alreadyMember) return (false, "Pehle se join hai");

        await DbHelper.ExecuteNonQueryAsync(conn,
            "INSERT INTO chat_room_members (room_id, user_id) VALUES (@rid, @uid)",
            new Dictionary<string, object?> { ["rid"] = roomId, ["uid"] = userId });

        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE chat_rooms SET member_count = member_count + 1 WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = roomId });

        return (true, "Room join ho gaya");
    }

    public async Task<(bool Success, string Message)> LeavePublicRoomAsync(Guid userId, Guid roomId)
    {
        await using var conn = await _db.CreateConnectionAsync();
        var rows = await DbHelper.ExecuteNonQueryAsync(conn,
            "DELETE FROM chat_room_members WHERE room_id = @rid AND user_id = @uid",
            new Dictionary<string, object?> { ["rid"] = roomId, ["uid"] = userId });

        if (rows > 0)
        {
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE chat_rooms SET member_count = GREATEST(member_count - 1, 0) WHERE id = @id",
                new Dictionary<string, object?> { ["id"] = roomId });
        }

        return rows > 0 ? (true, "Room leave kar diya") : (false, "Aap is room ke member nahi hain");
    }

    public async Task<List<StickerPackResponse>> GetStickerPacksAsync(Guid? userId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        var packs = await DbHelper.ExecuteReaderAsync(conn,
            "SELECT * FROM sticker_packs WHERE is_active = TRUE ORDER BY is_free DESC, coin_cost",
            r => new StickerPackResponse
            {
                Id = DbHelper.GetGuid(r, "id"),
                Name = DbHelper.GetString(r, "name"),
                ThumbnailUrl = DbHelper.GetStringOrNull(r, "thumbnail_url"),
                IsFree = DbHelper.GetBool(r, "is_free"),
                CoinCost = DbHelper.GetInt(r, "coin_cost"),
                IsOwned = DbHelper.GetBool(r, "is_free") // Free packs are owned by default
            });

        // Check which packs user owns (non-free)
        if (userId.HasValue)
        {
            var ownedPackIds = await DbHelper.ExecuteReaderAsync(conn,
                @"SELECT reference_id FROM coin_transactions
                  WHERE receiver_id = @uid AND reference_type = 'sticker_pack' AND status = 'completed'",
                r => r.IsDBNull(r.GetOrdinal("reference_id")) ? Guid.Empty : DbHelper.GetGuid(r, "reference_id"),
                new Dictionary<string, object?> { ["uid"] = userId.Value });

            var ownedSet = ownedPackIds.ToHashSet();
            foreach (var pack in packs)
                if (!pack.IsFree) pack.IsOwned = ownedSet.Contains(pack.Id);
        }

        // Load stickers for each pack
        foreach (var pack in packs)
        {
            pack.Stickers = await DbHelper.ExecuteReaderAsync(conn,
                "SELECT id, name, image_url, display_order FROM stickers WHERE pack_id = @pid ORDER BY display_order",
                r => new StickerResponse
                {
                    Id = DbHelper.GetGuid(r, "id"),
                    Name = DbHelper.GetStringOrNull(r, "name"),
                    ImageUrl = DbHelper.GetString(r, "image_url"),
                    DisplayOrder = DbHelper.GetInt(r, "display_order")
                },
                new Dictionary<string, object?> { ["pid"] = pack.Id });
        }

        return packs;
    }

    public async Task<(bool Success, string Message)> BuyStickerPackAsync(Guid userId, Guid packId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        var pack = await DbHelper.ExecuteReaderFirstAsync(conn,
            "SELECT id, name, coin_cost, is_free FROM sticker_packs WHERE id = @id AND is_active = TRUE",
            r => new { Id = DbHelper.GetGuid(r, "id"), Name = DbHelper.GetString(r, "name"), Cost = DbHelper.GetInt(r, "coin_cost"), IsFree = DbHelper.GetBool(r, "is_free") },
            new Dictionary<string, object?> { ["id"] = packId });

        if (pack == null) return (false, "Sticker pack nahi mila");
        if (pack.IsFree) return (false, "Yeh pack free hai, kharidne ki zarurat nahi");

        // Check already owned
        var alreadyOwned = await DbHelper.ExecuteScalarAsync<bool>(conn,
            "SELECT EXISTS(SELECT 1 FROM coin_transactions WHERE receiver_id = @uid AND reference_type = 'sticker_pack' AND reference_id = @pid AND status = 'completed')",
            new Dictionary<string, object?> { ["uid"] = userId, ["pid"] = packId });
        if (alreadyOwned) return (false, "Yeh pack pehle se hai aapke paas");

        // BUG#5 FIX: FOR UPDATE inside transaction — prevents race condition / negative balance
        using var transaction = await conn.BeginTransactionAsync();
        try
        {
            var balance = await DbHelper.ExecuteScalarAsync<long>(conn,
                "SELECT coin_balance FROM wallets WHERE user_id = @uid FOR UPDATE",
                new Dictionary<string, object?> { ["uid"] = userId });
            if (balance < pack.Cost)
            {
                await transaction.RollbackAsync();
                return (false, "Insufficient coins");
            }

            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE wallets SET coin_balance = coin_balance - @amount, total_spent = total_spent + @amount WHERE user_id = @uid",
                new Dictionary<string, object?> { ["uid"] = userId, ["amount"] = pack.Cost }, transaction);

            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO coin_transactions
                    (receiver_id, transaction_type, status, amount, reference_type, reference_id, description, completed_at)
                  VALUES (@uid, 'boost_purchase', 'completed', @amount, 'sticker_pack', @pid, @desc, NOW())",
                new Dictionary<string, object?>
                {
                    ["uid"] = userId,
                    ["amount"] = pack.Cost,
                    ["pid"] = packId,
                    ["desc"] = $"Sticker pack kharida: {pack.Name}"
                }, transaction);

            await transaction.CommitAsync();
            return (true, $"{pack.Name} sticker pack mil gaya!");
        }
        catch
        {
            await transaction.RollbackAsync();
            return (false, "Purchase mein error aaya");
        }
    }

    public async Task<(bool Success, string Message)> ReportMessageAsync(Guid userId, Guid messageId, ReportMessageRequest req)
    {
        await using var conn = await _db.CreateConnectionAsync();

        // BUG#M7-9 FIX: chat_reports table now stores room_id and a snapshot of the message
        // content at report time. Without the snapshot, if admin deletes the message before
        // reviewing, the context is lost. room_id enables admin to see which room it came from.
        await DbHelper.ExecuteNonQueryAsync(conn,
            @"CREATE TABLE IF NOT EXISTS chat_reports (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                message_id UUID NOT NULL,
                room_id UUID,
                sender_id UUID,
                reporter_id UUID NOT NULL,
                reason VARCHAR(50) NOT NULL DEFAULT 'other',
                description TEXT,
                message_snapshot TEXT,
                status VARCHAR(20) NOT NULL DEFAULT 'pending',
                resolved_by UUID,
                resolved_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                UNIQUE(message_id, reporter_id)
              )",
            new Dictionary<string, object?>());

        // Fetch message context (snapshot before possible deletion)
        var msg = await DbHelper.ExecuteReaderFirstAsync(conn,
            "SELECT room_id, sender_id, content FROM chat_messages WHERE id = @id AND is_deleted = FALSE",
            r => new
            {
                RoomId   = r.IsDBNull(r.GetOrdinal("room_id"))   ? (Guid?)null : DbHelper.GetGuid(r, "room_id"),
                SenderId = r.IsDBNull(r.GetOrdinal("sender_id")) ? (Guid?)null : DbHelper.GetGuid(r, "sender_id"),
                Content  = DbHelper.GetStringOrNull(r, "content")
            },
            new Dictionary<string, object?> { ["id"] = messageId });

        if (msg == null) return (false, "Message nahi mila");

        await DbHelper.ExecuteNonQueryAsync(conn,
            @"INSERT INTO chat_reports (message_id, room_id, sender_id, reporter_id, reason, description, message_snapshot)
              VALUES (@mid, @rid, @sid, @uid, @reason, @desc, @snapshot)
              ON CONFLICT (message_id, reporter_id) DO NOTHING",
            new Dictionary<string, object?>
            {
                ["mid"]      = messageId,
                ["rid"]      = (object?)msg.RoomId   ?? DBNull.Value,
                ["sid"]      = (object?)msg.SenderId  ?? DBNull.Value,
                ["uid"]      = userId,
                ["reason"]   = req.Reason,
                ["desc"]     = (object?)req.Description ?? DBNull.Value,
                ["snapshot"] = (object?)msg.Content   ?? DBNull.Value
            });

        return (true, "Report submit ho gaya. Team review karegi.");
    }
}
