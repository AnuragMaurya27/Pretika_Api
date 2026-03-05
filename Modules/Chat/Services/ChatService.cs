using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Infrastructure.Database;
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
    Task<(bool Success, string Message)> JoinPublicRoomAsync(Guid userId, Guid roomId);
    Task<(bool Success, string Message)> LeavePublicRoomAsync(Guid userId, Guid roomId);
    Task<List<StickerPackResponse>> GetStickerPacksAsync(Guid? userId);
    Task<(bool Success, string Message)> BuyStickerPackAsync(Guid userId, Guid packId);
}

public class ChatService : IChatService
{
    private readonly IDbConnectionFactory _db;
    private readonly IConfiguration _config;
    private readonly IHubContext<ChatHub> _hub;

    public ChatService(IDbConnectionFactory db, IConfiguration config, IHubContext<ChatHub> hub)
    {
        _db = db;
        _config = config;
        _hub = hub;
    }

    private static ChatMessageResponse MapMessage(NpgsqlDataReader r)
    {
        return new ChatMessageResponse
        {
            Id = DbHelper.GetGuid(r, "id"),
            RoomId = DbHelper.GetGuid(r, "room_id"),
            SenderId = DbHelper.GetGuid(r, "sender_id"),
            SenderUsername = DbHelper.GetString(r, "sender_username"),
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
        using var conn = await _db.CreateConnectionAsync();

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
        using var conn = await _db.CreateConnectionAsync();

        return await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT cr.id, cr.room_type, cr.last_message_at,
                     CASE WHEN cr.user1_id = @uid THEN cr.user2_id ELSE cr.user1_id END as other_user_id,
                     u.username as other_username, u.avatar_url as other_avatar_url
              FROM chat_rooms cr
              JOIN users u ON u.id = CASE WHEN cr.user1_id = @uid THEN cr.user2_id ELSE cr.user1_id END
              WHERE cr.room_type = 'private'
                AND (cr.user1_id = @uid OR cr.user2_id = @uid)
                AND cr.is_active = TRUE
              ORDER BY cr.last_message_at DESC NULLS LAST",
            r => new ChatRoomResponse
            {
                Id = DbHelper.GetGuid(r, "id"),
                RoomType = DbHelper.GetString(r, "room_type"),
                LastMessageAt = DbHelper.GetDateTimeOrNull(r, "last_message_at"),
                OtherUserId = DbHelper.GetGuid(r, "other_user_id"),
                OtherUsername = DbHelper.GetString(r, "other_username"),
                OtherAvatarUrl = DbHelper.GetStringOrNull(r, "other_avatar_url"),
                IsMember = true,
                IsActive = true
            },
            new Dictionary<string, object?> { ["uid"] = userId });
    }

    public async Task<(bool Success, string Message, ChatRoomResponse? Data)> StartPrivateChatAsync(Guid userId, StartPrivateChatRequest req)
    {
        if (req.TargetUserId == userId) return (false, "Apne aap se chat nahi kar sakte", null);

        using var conn = await _db.CreateConnectionAsync();

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
        using var conn = await _db.CreateConnectionAsync();

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

        var messages = await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT cm.*, u.username as sender_username, u.avatar_url as sender_avatar_url
              FROM chat_messages cm
              JOIN users u ON u.id = cm.sender_id
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
        using var conn = await _db.CreateConnectionAsync();

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
        }

        // Super chat handling
        Guid? txId = null;
        string? highlightColor = null;
        if (req.MessageType == "super_chat" && req.SuperChatCoins > 0)
        {
            var wallet = await DbHelper.ExecuteReaderFirstAsync(conn,
                "SELECT coin_balance, is_frozen FROM wallets WHERE user_id = @uid",
                r => new { Balance = DbHelper.GetLong(r, "coin_balance"), IsFrozen = DbHelper.GetBool(r, "is_frozen") },
                new Dictionary<string, object?> { ["uid"] = userId });

            if (wallet == null || wallet.IsFrozen) return (false, "Wallet nahi mila ya frozen hai", null);
            if (wallet.Balance < req.SuperChatCoins) return (false, "Insufficient coins", null);

            txId = Guid.NewGuid();
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO coin_transactions (id, sender_id, transaction_type, status, amount, reference_type, reference_id, description, completed_at)
                  VALUES (@id, @uid, 'super_chat', 'completed', @amount, 'chat_room', @rid, 'Super Chat', NOW())",
                new Dictionary<string, object?> { ["id"] = txId, ["uid"] = userId, ["amount"] = req.SuperChatCoins, ["rid"] = roomId });

            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE wallets SET coin_balance = coin_balance - @amount, total_spent = total_spent + @amount WHERE user_id = @uid",
                new Dictionary<string, object?> { ["uid"] = userId, ["amount"] = req.SuperChatCoins });

            highlightColor = req.SuperChatHighlightColor ?? "#FF4444";
        }

        var msgId = Guid.NewGuid();
        await DbHelper.ExecuteNonQueryAsync(conn,
            @"INSERT INTO chat_messages
                (id, room_id, sender_id, message_type, content, image_url, sticker_id,
                 is_super_chat, super_chat_coins, super_chat_highlight_color, transaction_id)
              VALUES
                (@id, @rid, @uid, @type::message_type, @content, @imgUrl, @stickerId,
                 @isSuperChat, @superCoins, @highlightColor, @txId)",
            new Dictionary<string, object?>
            {
                ["id"] = msgId,
                ["rid"] = roomId,
                ["uid"] = userId,
                ["type"] = req.MessageType,
                ["content"] = (object?)req.Content ?? DBNull.Value,
                ["imgUrl"] = (object?)req.ImageUrl ?? DBNull.Value,
                ["stickerId"] = (object?)req.StickerId ?? DBNull.Value,
                ["isSuperChat"] = req.MessageType == "super_chat",
                ["superCoins"] = req.SuperChatCoins,
                ["highlightColor"] = (object?)highlightColor ?? DBNull.Value,
                ["txId"] = (object?)txId ?? DBNull.Value
            });

        // Update room last_message_at
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE chat_rooms SET last_message_at = NOW() WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = roomId });

        var username = await DbHelper.ExecuteScalarAsync<string>(conn,
            "SELECT username FROM users WHERE id = @uid",
            new Dictionary<string, object?> { ["uid"] = userId });

        var msgResponse = new ChatMessageResponse
        {
            Id = msgId,
            RoomId = roomId,
            SenderId = userId,
            SenderUsername = username ?? "",
            MessageType = req.MessageType,
            Content = req.Content,
            ImageUrl = req.ImageUrl,
            StickerId = req.StickerId,
            IsSuperChat = req.MessageType == "super_chat",
            SuperChatCoins = req.SuperChatCoins,
            SuperChatHighlightColor = highlightColor,
            CreatedAt = DateTime.UtcNow
        };

        // Broadcast to all connected clients in this room via SignalR
        _ = _hub.Clients.Group(roomId.ToString()).SendAsync("NewMessage", msgResponse);

        return (true, "Message bheja", msgResponse);
    }

    public async Task<(bool Success, string Message)> DeleteMessageAsync(Guid userId, Guid messageId)
    {
        using var conn = await _db.CreateConnectionAsync();
        var rows = await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE chat_messages SET is_deleted = TRUE, deleted_by = @uid, deleted_at = NOW() WHERE id = @id AND sender_id = @uid AND is_deleted = FALSE",
            new Dictionary<string, object?> { ["id"] = messageId, ["uid"] = userId });
        return rows > 0 ? (true, "Message delete ho gaya") : (false, "Message nahi mila ya pehle se delete hai");
    }

    public async Task<(bool Success, string Message)> JoinPublicRoomAsync(Guid userId, Guid roomId)
    {
        using var conn = await _db.CreateConnectionAsync();

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
        using var conn = await _db.CreateConnectionAsync();
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
        using var conn = await _db.CreateConnectionAsync();

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
        using var conn = await _db.CreateConnectionAsync();

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

        // Check wallet
        var balance = await DbHelper.ExecuteScalarAsync<long>(conn,
            "SELECT coin_balance FROM wallets WHERE user_id = @uid",
            new Dictionary<string, object?> { ["uid"] = userId });
        if (balance < pack.Cost) return (false, "Insufficient coins");

        using var transaction = await conn.BeginTransactionAsync();
        try
        {
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
}
