using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Modules.Chat.Models;
using HauntedVoiceUniverse.Modules.Chat.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
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

    // DELETE /api/chat/admin/messages/{messageId}
    /// <summary>BUG#M7-7 FIX: Admin kisi bhi message ko delete kar sakta hai (sabke liye).</summary>
    [HttpDelete("admin/messages/{messageId:guid}")]
    [Authorize(Roles = "super_admin,moderator")]
    public async Task<IActionResult> AdminDeleteMessage(Guid messageId)
    {
        var (success, message) = await _chatService.AdminDeleteMessageAsync(RequiredUserId, messageId);
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

    // POST /api/chat/upload-image
    /// <summary>Chat ke liye image upload karo (JPG/PNG/WebP/GIF, max 5MB)</summary>
    // BUG#M7-5 FIX: RequestSizeLimit rejects the body at the HTTP layer before
    // buffering the full payload. Without this, a 100MB upload is received into
    // memory before the file.Length check can fire.
    [RequestSizeLimit(5 * 1024 * 1024 + 4096)]
    [HttpPost("upload-image")]
    [Authorize]
    public async Task<IActionResult> UploadChatImage(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("File select karo"));

        var allowed = new[] { "image/jpeg", "image/png", "image/webp", "image/gif" };
        if (!allowed.Contains(file.ContentType.ToLower()))
            return BadRequest(ApiResponse<object>.Fail("Sirf JPG, PNG, WebP ya GIF allowed hai"));

        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(ApiResponse<object>.Fail("Image 5MB se chhoti honi chahiye"));

        // BUG#M7-4 FIX: Validate magic bytes — Content-Type is client-controlled and can be
        // spoofed (e.g., rename malware.exe to image.jpg). Read the first 4 bytes and confirm
        // they match a known image signature before saving the file.
        var header = new byte[4];
        using (var peek = file.OpenReadStream())
            await peek.ReadAsync(header, 0, 4);

        bool validMagic =
            (header[0] == 0xFF && header[1] == 0xD8)                                           // JPEG  (FF D8)
            || (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47) // PNG   (89 50 4E 47)
            || (header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46)                   // GIF   (47 49 46)
            || (header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46); // WebP  (RIFF)

        if (!validMagic)
            return BadRequest(ApiResponse<object>.Fail("Invalid image file. Sirf real JPG/PNG/WebP/GIF allowed hai"));

        var env = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var wwwroot = env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var folder = Path.Combine(wwwroot, "chat-images");
        Directory.CreateDirectory(folder);

        // BUG#M7-4 FIX (part 2): Extension is forced from validated MIME type — not from the
        // original filename — so path traversal via filename tricks is impossible.
        var ext = file.ContentType.ToLower() switch
        {
            "image/jpeg" => ".jpg",
            "image/png"  => ".png",
            "image/webp" => ".webp",
            "image/gif"  => ".gif",
            _            => ".jpg"
        };
        var fileName = $"{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(folder, fileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
            await file.CopyToAsync(stream);

        var url = $"/chat-images/{fileName}";
        return Ok(ApiResponse<object>.Ok(new { url }, "Image upload ho gayi!"));
    }

    // POST /api/chat/messages/{messageId}/report
    /// <summary>Message report karo</summary>
    [HttpPost("messages/{messageId:guid}/report")]
    [Authorize]
    public async Task<IActionResult> ReportMessage(Guid messageId, [FromBody] ReportMessageRequest req)
    {
        var (success, message) = await _chatService.ReportMessageAsync(RequiredUserId, messageId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }
}
