using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Modules.Stories.Models;
using HauntedVoiceUniverse.Modules.Stories.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HauntedVoiceUniverse.Modules.Stories.Controllers;

[ApiController]
[Route("api/collections")]
[Produces("application/json")]
public class CollectionsController : ControllerBase
{
    private readonly IStoryService _storyService;

    public CollectionsController(IStoryService storyService)
    {
        _storyService = storyService;
    }

    private Guid? CurrentUserId => User.Identity?.IsAuthenticated == true
        ? Guid.TryParse(User.FindFirstValue("uid"), out var id) ? id : null
        : null;

    private Guid RequiredUserId => CurrentUserId
        ?? throw new UnauthorizedAccessException("Login required");

    // ─── MY COLLECTIONS ───────────────────────────────────────────────────────
    /// <summary>Apni saari collections dekho</summary>
    [HttpGet("my")]
    [Authorize]
    public async Task<IActionResult> GetMyCollections(
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
    {
        var result = await _storyService.GetMyCollectionsAsync(RequiredUserId, page, page_size);
        return Ok(ApiResponse<PagedResult<CollectionResponse>>.Ok(result));
    }

    // ─── CREATE ───────────────────────────────────────────────────────────────
    /// <summary>Nai collection banao</summary>
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateCollection([FromBody] CreateCollectionRequest req)
    {
        var (success, message, data) = await _storyService.CreateCollectionAsync(RequiredUserId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Created("", ApiResponse<CollectionResponse>.Created(data!, message));
    }

    // ─── UPDATE ───────────────────────────────────────────────────────────────
    /// <summary>Collection update karo</summary>
    [HttpPut("{collectionId:guid}")]
    [Authorize]
    public async Task<IActionResult> UpdateCollection(
        Guid collectionId, [FromBody] UpdateCollectionRequest req)
    {
        var (success, message, data) = await _storyService.UpdateCollectionAsync(
            RequiredUserId, collectionId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<CollectionResponse>.Ok(data!, message));
    }

    // ─── DELETE ───────────────────────────────────────────────────────────────
    /// <summary>Collection delete karo</summary>
    [HttpDelete("{collectionId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteCollection(Guid collectionId)
    {
        var (success, message) = await _storyService.DeleteCollectionAsync(RequiredUserId, collectionId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // ─── COLLECTION STORIES ───────────────────────────────────────────────────
    /// <summary>Collection ki stories dekho</summary>
    [HttpGet("{collectionId:guid}/stories")]
    public async Task<IActionResult> GetCollectionStories(
        Guid collectionId,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
    {
        var result = await _storyService.GetCollectionStoriesAsync(
            collectionId, CurrentUserId, page, page_size);
        return Ok(ApiResponse<PagedResult<StoryResponse>>.Ok(result));
    }

    // ─── ADD STORY ────────────────────────────────────────────────────────────
    /// <summary>Story ko collection mein add karo</summary>
    [HttpPost("{collectionId:guid}/stories/{storyId:guid}")]
    [Authorize]
    public async Task<IActionResult> AddStory(Guid collectionId, Guid storyId)
    {
        var (success, message) = await _storyService.AddStoryToCollectionAsync(
            RequiredUserId, collectionId, storyId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // ─── REMOVE STORY ─────────────────────────────────────────────────────────
    /// <summary>Story ko collection se remove karo</summary>
    [HttpDelete("{collectionId:guid}/stories/{storyId:guid}")]
    [Authorize]
    public async Task<IActionResult> RemoveStory(Guid collectionId, Guid storyId)
    {
        var (success, message) = await _storyService.RemoveStoryFromCollectionAsync(
            RequiredUserId, collectionId, storyId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }
}
