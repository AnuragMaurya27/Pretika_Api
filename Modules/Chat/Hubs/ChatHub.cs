using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace HauntedVoiceUniverse.Modules.Chat.Hubs;

[Authorize]
public class ChatHub : Hub
{
    /// <summary>Client joins a chat room</summary>
    public async Task JoinRoom(string roomId)
    {
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
