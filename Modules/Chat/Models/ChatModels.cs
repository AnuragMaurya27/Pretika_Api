using System.ComponentModel.DataAnnotations;

namespace HauntedVoiceUniverse.Modules.Chat.Models;

public class ChatRoomResponse
{
    public Guid Id { get; set; }
    public string RoomType { get; set; } = "";
    public string? Name { get; set; }
    public string? NameHi { get; set; }
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public int MemberCount { get; set; }
    public bool IsActive { get; set; }
    public bool IsRestricted { get; set; }
    public DateTime? LastMessageAt { get; set; }

    // For private chats
    public Guid? OtherUserId { get; set; }
    public string? OtherUsername { get; set; }
    public string? OtherAvatarUrl { get; set; }

    // Viewer context
    public bool IsMember { get; set; }
    public ChatMessageResponse? LastMessage { get; set; }
}

public class ChatMessageResponse
{
    public Guid Id { get; set; }
    public Guid RoomId { get; set; }
    public Guid SenderId { get; set; }
    public string SenderUsername { get; set; } = "";
    public string? SenderAvatarUrl { get; set; }
    public string MessageType { get; set; } = "";
    public string? Content { get; set; }
    public string? ImageUrl { get; set; }
    public string? StickerId { get; set; }
    public bool IsSuperChat { get; set; }
    public int SuperChatCoins { get; set; }
    public string? SuperChatHighlightColor { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsSystemMessage { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class StickerPackResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? ThumbnailUrl { get; set; }
    public bool IsFree { get; set; }
    public int CoinCost { get; set; }
    public bool IsOwned { get; set; }
    public List<StickerResponse> Stickers { get; set; } = new();
}

public class StickerResponse
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string ImageUrl { get; set; } = "";
    public int DisplayOrder { get; set; }
}

public class SendMessageRequest
{
    [Required]
    public string MessageType { get; set; } = "text"; // text, sticker, image, super_chat

    [StringLength(2000)]
    public string? Content { get; set; }

    public string? ImageUrl { get; set; }
    public string? StickerId { get; set; }

    // Super chat (0 = no super chat, valid for normal messages)
    [Range(0, 10000)]
    public int SuperChatCoins { get; set; } = 0;
    public string? SuperChatHighlightColor { get; set; }
}

public class StartPrivateChatRequest
{
    [Required]
    public Guid TargetUserId { get; set; }
}
