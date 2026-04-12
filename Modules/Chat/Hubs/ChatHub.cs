using HauntedVoiceUniverse.Infrastructure.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace HauntedVoiceUniverse.Modules.Chat.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly IDbConnectionFactory _db;

    public ChatHub(IDbConnectionFactory db)
    {
        _db = db;
    }

    private Guid? CurrentUserId =>
        Guid.TryParse(Context.User?.FindFirstValue("uid"), out var id) ? id : null;

    /// <summary>Client joins a chat room</summary>
    public async Task JoinRoom(string roomId)
    {
        // VULN#6 FIX: Verify the user has access to this room before adding to the group.
        // Without this check, any authenticated user can subscribe to any private room's
        // real-time messages, including private chats they are not a party to.
        var userId = CurrentUserId;
        if (userId == null) throw new HubException("Unauthorized");

        if (!Guid.TryParse(roomId, out var roomGuid))
            throw new HubException("Invalid room ID format");

        await using var conn = await _db.CreateConnectionAsync();
        var hasAccess = await DbHelper.ExecuteScalarAsync<bool>(conn,
            @"SELECT EXISTS(
                SELECT 1 FROM chat_rooms cr
                WHERE cr.id = @rid AND cr.is_active = TRUE
                  AND (cr.room_type = 'public'
                    OR (cr.room_type = 'private'
                        AND (cr.user1_id = @uid OR cr.user2_id = @uid)))
              )",
            new Dictionary<string, object?> { ["rid"] = roomGuid, ["uid"] = userId.Value });

        if (!hasAccess)
            throw new HubException("Is room mein access nahi hai");

        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
    }

    /// <summary>Client leaves a chat room</summary>
    public async Task LeaveRoom(string roomId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);
    }

    /// <summary>Typing indicator — broadcast to room except sender</summary>
    public async Task Typing(TypingPayload payload)
    {
        var userId = Context.User?.FindFirstValue("uid") ?? "";
        var username = Context.User?.FindFirstValue(ClaimTypes.Name)
                    ?? Context.User?.FindFirstValue("username")
                    ?? "Someone";

        await Clients.OthersInGroup(payload.RoomId).SendAsync("UserTyping", new
        {
            UserId = userId,
            Username = username,
            RoomId = payload.RoomId,
        });
    }

    /// <summary>Mark messages as seen — broadcast to room</summary>
    public async Task MarkSeen(MarkSeenPayload payload)
    {
        var userId = Context.User?.FindFirstValue("uid") ?? "";
        await Clients.OthersInGroup(payload.RoomId).SendAsync("MessageSeen", new
        {
            UserId = userId,
            RoomId = payload.RoomId,
            MessageIds = payload.MessageIds,
        });
    }
}

public record TypingPayload(string RoomId);
public record MarkSeenPayload(string RoomId, List<string> MessageIds);
