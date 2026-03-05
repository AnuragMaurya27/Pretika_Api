using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Modules.Notifications.Models;
using HauntedVoiceUniverse.Modules.Notifications.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HauntedVoiceUniverse.Modules.Notifications.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
[Produces("application/json")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    private Guid UserId => Guid.Parse(User.FindFirstValue("uid")!);

    // GET /api/notifications
    /// <summary>Meri notifications</summary>
    [HttpGet]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] bool? unread_only,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
    {
        var result = await _notificationService.GetNotificationsAsync(UserId, unread_only, page, page_size);
        return Ok(ApiResponse<PagedResult<NotificationResponse>>.Ok(result));
    }

    // GET /api/notifications/unread-count
    /// <summary>Unread notifications count</summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        var result = await _notificationService.GetUnreadCountAsync(UserId);
        return Ok(ApiResponse<UnreadCountResponse>.Ok(result));
    }

    // POST /api/notifications/{id}/read
    /// <summary>Notification read mark karo</summary>
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id)
    {
        var (success, message) = await _notificationService.MarkAsReadAsync(UserId, id);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // POST /api/notifications/read-all
    /// <summary>Saari notifications read mark karo</summary>
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var (success, message) = await _notificationService.MarkAllReadAsync(UserId);
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // DELETE /api/notifications/{id}
    /// <summary>Notification delete karo</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteNotification(Guid id)
    {
        var (success, message) = await _notificationService.DeleteNotificationAsync(UserId, id);
        if (!success) return NotFound(ApiResponse<object>.NotFound(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // GET /api/notifications/preferences
    /// <summary>Notification preferences dekho</summary>
    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences()
    {
        var prefs = await _notificationService.GetPreferencesAsync(UserId);
        return Ok(ApiResponse<NotificationPreferencesResponse>.Ok(prefs));
    }

    // PUT /api/notifications/preferences
    /// <summary>Notification preferences update karo</summary>
    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdateNotificationPreferencesRequest req)
    {
        var (success, message) = await _notificationService.UpdatePreferencesAsync(UserId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }
}

// Separate controller for announcements (no auth required)
[ApiController]
[Route("api/announcements")]
[Produces("application/json")]
public class AnnouncementsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public AnnouncementsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    // GET /api/announcements
    /// <summary>Active announcements dekho</summary>
    [HttpGet]
    public async Task<IActionResult> GetAnnouncements()
    {
        var announcements = await _notificationService.GetActiveAnnouncementsAsync();
        return Ok(ApiResponse<List<AnnouncementResponse>>.Ok(announcements));
    }
}
