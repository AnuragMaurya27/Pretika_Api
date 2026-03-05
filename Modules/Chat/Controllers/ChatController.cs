using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Modules.Chat.Models;
using HauntedVoiceUniverse.Modules.Chat.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HauntedVoiceUniverse.Modules.Chat.Controllers;

[ApiController]
[Route("api/chat")]
[Produces("application/json")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;

    public ChatController(IChatService chatService)
    {
        _chatService = chatService;
    }

    private Guid? CurrentUserId => User.Identity?.IsAuthenticated == true
        ? Guid.TryParse(User.FindFirstValue("uid"), out var id) ? id : null
        : null;

    private Guid RequiredUserId => CurrentUserId
        ?? throw new UnauthorizedAccessException("Login required");

    // GET /api/chat/rooms/public
    /// <summary>Public chat rooms dekho</summary>
    [HttpGet("rooms/public")]
    public async Task<IActionResult> GetPublicRooms()
    {
        var rooms = await _chatService.GetPublicRoomsAsync(CurrentUserId);
        return Ok(ApiResponse<List<ChatRoomResponse>>.Ok(rooms));
    }

    // GET /api/chat/rooms/private
    /// <summary>Apne private chats dekho</summary>
    [HttpGet("rooms/private")]
    [Authorize]
    public async Task<IActionResult> GetPrivateChats()
    {
        var rooms = await _chatService.GetMyPrivateChatsAsync(RequiredUserId);
        return Ok(ApiResponse<List<ChatRoomResponse>>.Ok(rooms));
    }

    // POST /api/chat/rooms/private
    /// <summary>Private chat shuru karo kisi ke saath</summary>
    [HttpPost("rooms/private")]
    [Authorize]
    public async Task<IActionResult> StartPrivateChat([FromBody] StartPrivateChatRequest req)
    {
        var (success, message, data) = await _chatService.StartPrivateChatAsync(RequiredUserId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<ChatRoomResponse>.Ok(data!, message));
    }

    // GET /api/chat/rooms/{roomId}/messages
    /// <summary>Room ke messages dekho</summary>
    [HttpGet("rooms/{roomId:guid}/messages")]
    [Authorize]
    public async Task<IActionResult> GetMessages(
        Guid roomId,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 50)
    {
        var result = await _chatService.GetMessagesAsync(RequiredUserId, roomId, page, page_size);
        return Ok(ApiResponse<PagedResult<ChatMessageResponse>>.Ok(result));
    }

    // POST /api/chat/rooms/{roomId}/messages
    /// <summary>Message bhejo</summary>
    [HttpPost("rooms/{roomId:guid}/messages")]
    [Authorize]
    public async Task<IActionResult> SendMessage(Guid roomId, [FromBody] SendMessageRequest req)
    {
        var (success, message, data) = await _chatService.SendMessageAsync(RequiredUserId, roomId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<ChatMessageResponse>.Ok(data!, message));
    }

    // DELETE /api/chat/messages/{messageId}
    /// <summary>Apna message delete karo</summary>
    [HttpDelete("messages/{messageId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteMessage(Guid messageId)
    {
        var (success, message) = await _chatService.DeleteMessageAsync(RequiredUserId, messageId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // POST /api/chat/rooms/{roomId}/join
    /// <summary>Public room join karo</summary>
    [HttpPost("rooms/{roomId:guid}/join")]
    [Authorize]
    public async Task<IActionResult> JoinRoom(Guid roomId)
    {
        var (success, message) = await _chatService.JoinPublicRoomAsync(RequiredUserId, roomId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // POST /api/chat/rooms/{roomId}/leave
    /// <summary>Room leave karo</summary>
    [HttpPost("rooms/{roomId:guid}/leave")]
    [Authorize]
    public async Task<IActionResult> LeaveRoom(Guid roomId)
    {
        var (success, message) = await _chatService.LeavePublicRoomAsync(RequiredUserId, roomId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // GET /api/chat/sticker-packs
    /// <summary>Sticker packs dekho</summary>
    [HttpGet("sticker-packs")]
    public async Task<IActionResult> GetStickerPacks()
    {
        var packs = await _chatService.GetStickerPacksAsync(CurrentUserId);
        return Ok(ApiResponse<List<StickerPackResponse>>.Ok(packs));
    }

    // POST /api/chat/sticker-packs/{id}/buy
    /// <summary>Sticker pack kharido</summary>
    [HttpPost("sticker-packs/{id:guid}/buy")]
    [Authorize]
    public async Task<IActionResult> BuyStickerPack(Guid id)
    {
        var (success, message) = await _chatService.BuyStickerPackAsync(RequiredUserId, id);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }
}
