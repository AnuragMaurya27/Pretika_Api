using System.ComponentModel.DataAnnotations;

namespace HauntedVoiceUniverse.Modules.Notifications.Models;

public class NotificationResponse
{
    public Guid Id { get; set; }
    public string NotificationType { get; set; } = "";
    public string Channel { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? ImageUrl { get; set; }
    public string? ActionUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Actor info
    public Guid? ActorId { get; set; }
    public string? ActorUsername { get; set; }
    public string? ActorAvatarUrl { get; set; }
}

public class AnnouncementResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? BannerUrl { get; set; }
    public string? DisplayType { get; set; }
    public int Priority { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
}

public class NotificationPreferencesResponse
{
    public bool EmailNewFollower { get; set; }
    public bool EmailNewComment { get; set; }
    public bool EmailNewEpisode { get; set; }
    public bool EmailCoinReceived { get; set; }
    public bool EmailAnnouncements { get; set; }
    public bool PushNewFollower { get; set; }
    public bool PushNewComment { get; set; }
    public bool PushNewEpisode { get; set; }
    public bool PushCoinReceived { get; set; }
    public bool PushChatMessage { get; set; }
    public bool PushAnnouncements { get; set; }
    public string? QuietHoursStart { get; set; }
    public string? QuietHoursEnd { get; set; }
}

public class UpdateNotificationPreferencesRequest
{
    public bool? EmailNewFollower { get; set; }
    public bool? EmailNewComment { get; set; }
    public bool? EmailNewEpisode { get; set; }
    public bool? EmailCoinReceived { get; set; }
    public bool? EmailAnnouncements { get; set; }
    public bool? PushNewFollower { get; set; }
    public bool? PushNewComment { get; set; }
    public bool? PushNewEpisode { get; set; }
    public bool? PushCoinReceived { get; set; }
    public bool? PushChatMessage { get; set; }
    public bool? PushAnnouncements { get; set; }
    public string? QuietHoursStart { get; set; }
    public string? QuietHoursEnd { get; set; }
}

public class UnreadCountResponse
{
    public int UnreadCount { get; set; }
}
