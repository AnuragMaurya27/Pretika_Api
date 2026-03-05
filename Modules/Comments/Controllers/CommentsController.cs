using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Modules.Comments.Models;
using HauntedVoiceUniverse.Modules.Comments.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HauntedVoiceUniverse.Modules.Comments.Controllers;

[ApiController]
[Route("api")]
[Produces("application/json")]
public class CommentsController : ControllerBase
{
    private readonly ICommentService _commentService;

    public CommentsController(ICommentService commentService)
    {
        _commentService = commentService;
    }

    private Guid? CurrentUserId => User.Identity?.IsAuthenticated == true
        ? Guid.TryParse(User.FindFirstValue("uid"), out var id) ? id : null
        : null;

    private Guid RequiredUserId => CurrentUserId
        ?? throw new UnauthorizedAccessException("Login required");

    // GET /api/stories/{storyId}/comments
    /// <summary>Story ke comments dekho</summary>
    [HttpGet("stories/{storyId:guid}/comments")]
    public async Task<IActionResult> GetStoryComments(
        Guid storyId,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
    {
        var result = await _commentService.GetStoryCommentsAsync(storyId, CurrentUserId, page, page_size);
        return Ok(ApiResponse<PagedResult<CommentResponse>>.Ok(result));
    }

    // POST /api/stories/{storyId}/comments
    /// <summary>Comment likho (login required)</summary>
    [HttpPost("stories/{storyId:guid}/comments")]
    [Authorize]
    public async Task<IActionResult> CreateComment(Guid storyId, [FromBody] CreateCommentRequest req)
    {
        req.StoryId = storyId;
        var (success, message, data) = await _commentService.CreateCommentAsync(RequiredUserId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Created("", ApiResponse<CommentResponse>.Created(data!, message));
    }

    // GET /api/comments/{commentId}/replies
    /// <summary>Comment ke replies dekho</summary>
    [HttpGet("comments/{commentId:guid}/replies")]
    public async Task<IActionResult> GetReplies(
        Guid commentId,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
    {
        var result = await _commentService.GetRepliesAsync(commentId, CurrentUserId, page, page_size);
        return Ok(ApiResponse<PagedResult<CommentResponse>>.Ok(result));
    }

    // DELETE /api/comments/{commentId}
    /// <summary>Apna comment delete karo</summary>
    [HttpDelete("comments/{commentId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteComment(Guid commentId)
    {
        var (success, message) = await _commentService.DeleteCommentAsync(RequiredUserId, commentId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // POST /api/comments/{commentId}/like
    /// <summary>Comment like karo</summary>
    [HttpPost("comments/{commentId:guid}/like")]
    [Authorize]
    public async Task<IActionResult> LikeComment(Guid commentId)
    {
        var (success, message) = await _commentService.LikeCommentAsync(RequiredUserId, commentId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // DELETE /api/comments/{commentId}/like
    /// <summary>Comment unlike karo</summary>
    [HttpDelete("comments/{commentId:guid}/like")]
    [Authorize]
    public async Task<IActionResult> UnlikeComment(Guid commentId)
    {
        var (success, message) = await _commentService.UnlikeCommentAsync(RequiredUserId, commentId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // POST /api/comments/{commentId}/report
    /// <summary>Comment report karo</summary>
    [HttpPost("comments/{commentId:guid}/report")]
    [Authorize]
    public async Task<IActionResult> ReportComment(Guid commentId, [FromBody] ReportCommentRequest req)
    {
        var (success, message) = await _commentService.ReportCommentAsync(RequiredUserId, commentId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // POST /api/stories/{storyId}/comments/{commentId}/pin
    /// <summary>Comment pin karo (story creator only)</summary>
    [HttpPost("stories/{storyId:guid}/comments/{commentId:guid}/pin")]
    [Authorize]
    public async Task<IActionResult> PinComment(Guid storyId, Guid commentId)
    {
        var (success, message) = await _commentService.PinCommentAsync(RequiredUserId, commentId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // DELETE /api/stories/{storyId}/comments/{commentId}/pin
    /// <summary>Comment unpin karo (story creator only)</summary>
    [HttpDelete("stories/{storyId:guid}/comments/{commentId:guid}/pin")]
    [Authorize]
    public async Task<IActionResult> UnpinComment(Guid storyId, Guid commentId)
    {
        var (success, message) = await _commentService.UnpinCommentAsync(RequiredUserId, commentId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }
}
