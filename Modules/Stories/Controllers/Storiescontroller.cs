using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Modules.Stories.Models;
using HauntedVoiceUniverse.Modules.Stories.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HauntedVoiceUniverse.Modules.Stories.Controllers;

[ApiController]
[Route("api/stories")]
[Produces("application/json")]
public class StoriesController : ControllerBase
{
    private readonly IStoryService _storyService;

    public StoriesController(IStoryService storyService)
    {
        _storyService = storyService;
    }

    private Guid? CurrentUserId => User.Identity?.IsAuthenticated == true
        ? Guid.TryParse(User.FindFirstValue("uid"), out var id) ? id : null
        : null;

    private Guid RequiredUserId => CurrentUserId
        ?? throw new UnauthorizedAccessException("Login required");

    // ─── STORIES FEED ─────────────────────────────────────────────────────────
    /// <summary>Stories feed - filter, search, sort</summary>
    [HttpGet]
    public async Task<IActionResult> GetStories([FromQuery] StoryFilterRequest filter)
    {
        var result = await _storyService.GetStoriesAsync(filter, CurrentUserId);
        return Ok(ApiResponse<PagedResult<StoryResponse>>.Ok(result));
    }

    /// <summary>Categories list</summary>
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories()
    {
        var result = await _storyService.GetCategoriesAsync();
        return Ok(ApiResponse<List<CategoryResponse>>.Ok(result));
    }

    // VULN#7 FIX: Category management restricted to admin roles only.
    // Previously [Authorize] only — any logged-in user could create/delete categories,
    // spamming or vandalizing the global category taxonomy.
    /// <summary>Nai category banao (admin only)</summary>
    [HttpPost("categories")]
    [Authorize(Roles = "super_admin,moderator")]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest req)
    {
        var (success, message, data) = await _storyService.CreateCategoryAsync(req.Name, req.Description, req.IconUrl);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<CategoryResponse>.Ok(data!, message));
    }

    /// <summary>Category delete karo (admin only)</summary>
    [HttpDelete("categories/{categoryId:guid}")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> DeleteCategory(Guid categoryId)
    {
        var (success, message) = await _storyService.DeleteCategoryAsync(categoryId);
        if (!success) return NotFound(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    /// <summary>Apni saari stories (creator)</summary>
    [HttpGet("my")]
    [Authorize]
    public async Task<IActionResult> GetMyStories(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
    {
        var result = await _storyService.GetMyStoriesAsync(RequiredUserId, status, page, page_size);
        return Ok(ApiResponse<PagedResult<StoryResponse>>.Ok(result));
    }

    /// <summary>Bookmarked stories</summary>
    [HttpGet("bookmarked")]
    [Authorize]
    public async Task<IActionResult> GetBookmarkedStories(
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
    {
        var result = await _storyService.GetBookmarkedStoriesAsync(RequiredUserId, page, page_size);
        return Ok(ApiResponse<PagedResult<StoryResponse>>.Ok(result));
    }

    /// <summary>Kisi creator ki published stories</summary>
    [HttpGet("creator/{creatorId:guid}")]
    public async Task<IActionResult> GetCreatorStories(
        Guid creatorId,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
    {
        var result = await _storyService.GetCreatorStoriesAsync(creatorId, CurrentUserId, page, page_size);
        return Ok(ApiResponse<PagedResult<StoryResponse>>.Ok(result));
    }

    // ─── STORY CRUD ───────────────────────────────────────────────────────────
    /// <summary>Nai story create karo (creator only)</summary>
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateStory([FromBody] CreateStoryRequest req)
    {
        var (success, message, data) = await _storyService.CreateStoryAsync(RequiredUserId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Created("", ApiResponse<StoryResponse>.Created(data!, message));
    }

    /// <summary>Story slug se dekho</summary>
    [HttpGet("{slug}")]
    public async Task<IActionResult> GetStoryBySlug(string slug)
    {
        var story = await _storyService.GetStoryBySlugAsync(slug, CurrentUserId);
        if (story == null) return NotFound(ApiResponse<object>.NotFound("Story nahi mili"));

        // View record karo (background mein)
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        _ = _storyService.RecordViewAsync(story.Id, null, CurrentUserId, ip,
            Request.Headers["User-Agent"].ToString());

        return Ok(ApiResponse<StoryDetailResponse>.Ok(story));
    }

    /// <summary>Story ID se dekho</summary>
    [HttpGet("id/{storyId:guid}")]
    public async Task<IActionResult> GetStoryById(Guid storyId)
    {
        var story = await _storyService.GetStoryByIdAsync(storyId, CurrentUserId);
        if (story == null) return NotFound(ApiResponse<object>.NotFound("Story nahi mili"));
        return Ok(ApiResponse<StoryDetailResponse>.Ok(story));
    }

    /// <summary>Story update karo</summary>
    [HttpPut("{storyId:guid}")]
    [Authorize]
    public async Task<IActionResult> UpdateStory(Guid storyId, [FromBody] UpdateStoryRequest req)
    {
        var (success, message, data) = await _storyService.UpdateStoryAsync(RequiredUserId, storyId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<StoryResponse>.Ok(data!, message));
    }

    /// <summary>Story delete karo</summary>
    [HttpDelete("{storyId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteStory(Guid storyId)
    {
        var (success, message) = await _storyService.DeleteStoryAsync(RequiredUserId, storyId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    /// <summary>Story publish karo</summary>
    [HttpPost("{storyId:guid}/publish")]
    [Authorize]
    public async Task<IActionResult> PublishStory(Guid storyId)
    {
        var (success, message) = await _storyService.PublishStoryAsync(RequiredUserId, storyId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    /// <summary>Story unpublish karo</summary>
    [HttpPost("{storyId:guid}/unpublish")]
    [Authorize]
    public async Task<IActionResult> UnpublishStory(Guid storyId)
    {
        var (success, message) = await _storyService.UnpublishStoryAsync(RequiredUserId, storyId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // ─── RATING ───────────────────────────────────────────────────────────────
    /// <summary>Story ko rate karo (1-5 stars)</summary>
    [HttpPost("{storyId:guid}/rate")]
    [Authorize]
    public async Task<IActionResult> RateStory(Guid storyId, [FromBody] RateStoryRequest req)
    {
        var (success, message, data) = await _storyService.RateStoryAsync(RequiredUserId, storyId, req.Rating);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(data, message));
    }

    // ─── LIKE / BOOKMARK ──────────────────────────────────────────────────────
    /// <summary>Story like karo</summary>
    [HttpPost("{storyId:guid}/like")]
    [Authorize]
    public async Task<IActionResult> LikeStory(Guid storyId)
    {
        var (success, message) = await _storyService.LikeStoryAsync(RequiredUserId, storyId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    /// <summary>Story unlike karo</summary>
    [HttpDelete("{storyId:guid}/like")]
    [Authorize]
    public async Task<IActionResult> UnlikeStory(Guid storyId)
    {
        var (success, message) = await _storyService.UnlikeStoryAsync(RequiredUserId, storyId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    /// <summary>Story bookmark karo</summary>
    [HttpPost("{storyId:guid}/bookmark")]
    [Authorize]
    public async Task<IActionResult> BookmarkStory(Guid storyId)
    {
        var (success, message) = await _storyService.BookmarkStoryAsync(RequiredUserId, storyId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    /// <summary>Story unbookmark karo</summary>
    [HttpDelete("{storyId:guid}/bookmark")]
    [Authorize]
    public async Task<IActionResult> UnbookmarkStory(Guid storyId)
    {
        var (success, message) = await _storyService.UnbookmarkStoryAsync(RequiredUserId, storyId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // ─── EPISODES ─────────────────────────────────────────────────────────────
    /// <summary>Story ke saare episodes list karo</summary>
    [HttpGet("{storyId:guid}/episodes")]
    public async Task<IActionResult> GetEpisodes(Guid storyId)
    {
        var episodes = await _storyService.GetEpisodesAsync(storyId, CurrentUserId);
        return Ok(ApiResponse<List<EpisodeResponse>>.Ok(episodes));
    }

    /// <summary>Naya episode add karo</summary>
    [HttpPost("{storyId:guid}/episodes")]
    [Authorize]
    public async Task<IActionResult> CreateEpisode(Guid storyId, [FromBody] CreateEpisodeRequest req)
    {
        var (success, message, data) = await _storyService.CreateEpisodeAsync(RequiredUserId, storyId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Created("", ApiResponse<EpisodeResponse>.Created(data!, message));
    }

    /// <summary>Episode padho</summary>
    [HttpGet("{storyId:guid}/episodes/{episodeId:guid}")]
    public async Task<IActionResult> GetEpisode(Guid storyId, Guid episodeId)
    {
        var episode = await _storyService.GetEpisodeAsync(storyId, episodeId, CurrentUserId);
        if (episode == null) return NotFound(ApiResponse<object>.NotFound("Episode nahi mila"));

        // View record
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        _ = _storyService.RecordViewAsync(storyId, episodeId, CurrentUserId, ip,
            Request.Headers["User-Agent"].ToString());

        return Ok(ApiResponse<EpisodeResponse>.Ok(episode));
    }

    /// <summary>Episode update karo</summary>
    [HttpPut("{storyId:guid}/episodes/{episodeId:guid}")]
    [Authorize]
    public async Task<IActionResult> UpdateEpisode(
        Guid storyId, Guid episodeId, [FromBody] UpdateEpisodeRequest req)
    {
        var (success, message, data) = await _storyService.UpdateEpisodeAsync(
            RequiredUserId, storyId, episodeId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<EpisodeResponse>.Ok(data!, message));
    }

    /// <summary>Episode delete karo</summary>
    [HttpDelete("{storyId:guid}/episodes/{episodeId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteEpisode(Guid storyId, Guid episodeId)
    {
        var (success, message) = await _storyService.DeleteEpisodeAsync(
            RequiredUserId, storyId, episodeId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    /// <summary>Episode publish karo</summary>
    [HttpPost("{storyId:guid}/episodes/{episodeId:guid}/publish")]
    [Authorize]
    public async Task<IActionResult> PublishEpisode(Guid storyId, Guid episodeId)
    {
        var (success, message) = await _storyService.PublishEpisodeAsync(
            RequiredUserId, storyId, episodeId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    /// <summary>Episode like karo</summary>
    [HttpPost("{storyId:guid}/episodes/{episodeId:guid}/like")]
    [Authorize]
    public async Task<IActionResult> LikeEpisode(Guid storyId, Guid episodeId)
    {
        var (success, message) = await _storyService.LikeEpisodeAsync(RequiredUserId, storyId, episodeId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    /// <summary>Episode unlike karo</summary>
    [HttpDelete("{storyId:guid}/episodes/{episodeId:guid}/like")]
    [Authorize]
    public async Task<IActionResult> UnlikeEpisode(Guid storyId, Guid episodeId)
    {
        var (success, message) = await _storyService.UnlikeEpisodeAsync(RequiredUserId, storyId, episodeId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    /// <summary>Episode ko rate karo (1-5 stars)</summary>
    [HttpPost("{storyId:guid}/episodes/{episodeId:guid}/rate")]
    [Authorize]
    public async Task<IActionResult> RateEpisode(Guid storyId, Guid episodeId, [FromBody] RateStoryRequest req)
    {
        var (success, message, data) = await _storyService.RateEpisodeAsync(RequiredUserId, storyId, episodeId, req.Rating);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(data, message));
    }

    /// <summary>Episode complete mark karo — +25 reader XP milega (ek baar)</summary>
    [HttpPost("{storyId:guid}/episodes/{episodeId:guid}/complete")]
    [Authorize]
    public async Task<IActionResult> CompleteEpisode(Guid storyId, Guid episodeId)
    {
        var (success, message) = await _storyService.CompleteEpisodeAsync(RequiredUserId, storyId, episodeId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    /// <summary>Premium episode unlock karo coins se</summary>
    [HttpPost("episodes/{episodeId:guid}/unlock")]
    [Authorize]
    public async Task<IActionResult> UnlockEpisode(Guid episodeId)
    {
        var (success, message) = await _storyService.UnlockEpisodeAsync(RequiredUserId, episodeId);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // ─── THUMBNAIL UPLOAD ─────────────────────────────────────────────────────
    /// <summary>Story thumbnail image upload karo (JPG/PNG/WebP, max 5MB)</summary>
    // BUG#M3-10 FIX: Limit request size to 6MB so 50MB uploads are rejected
    // before they're fully buffered in memory.
    [HttpPost("upload-thumbnail")]
    [Authorize]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> UploadThumbnail(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("File select karo"));

        // BUG#M3-2 FIX: Do NOT trust Content-Type — validate actual magic bytes.
        // An attacker can rename any file to .jpg and set Content-Type: image/jpeg.
        var magicResult = await DetectImageTypeAsync(file);
        if (magicResult == null)
            return BadRequest(ApiResponse<object>.Fail("Invalid file. Sirf JPG, PNG, WebP ya GIF allowed hai"));

        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(ApiResponse<object>.Fail("Image 5MB se chhoti honi chahiye"));

        var wwwroot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var folder = Path.Combine(wwwroot, "thumbnail");
        Directory.CreateDirectory(folder);

        // Always use the magic-byte-detected extension, never the original filename
        var fileName = $"{Guid.NewGuid()}{magicResult}";
        var filePath = Path.Combine(folder, fileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
            await file.CopyToAsync(stream);

        var url = $"/thumbnail/{fileName}";
        return Ok(ApiResponse<object>.Ok(new { url }, "Thumbnail upload ho gaya!"));
    }

    // ─── CONTENT IMAGE UPLOAD ─────────────────────────────────────────────────
    /// <summary>Episode content ke liye image upload karo (JPG/PNG/WebP, max 5MB)</summary>
    [HttpPost("upload-image")]
    [Authorize]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> UploadContentImage(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("File select karo"));

        var magicResult = await DetectImageTypeAsync(file);
        if (magicResult == null)
            return BadRequest(ApiResponse<object>.Fail("Invalid file. Sirf JPG, PNG, WebP ya GIF allowed hai"));

        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(ApiResponse<object>.Fail("Image 5MB se chhoti honi chahiye"));

        var wwwroot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var folder = Path.Combine(wwwroot, "content-images");
        Directory.CreateDirectory(folder);

        var fileName = $"{Guid.NewGuid()}{magicResult}";
        var filePath = Path.Combine(folder, fileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
            await file.CopyToAsync(stream);

        var url = $"/content-images/{fileName}";
        return Ok(ApiResponse<object>.Ok(new { url }, "Image upload ho gayi!"));
    }

    /// <summary>
    /// BUG#M3-2 FIX: Read first 12 bytes and compare against known image magic numbers.
    /// Returns the canonical extension (e.g. ".jpg") or null if not a recognised image.
    /// </summary>
    private static async Task<string?> DetectImageTypeAsync(IFormFile file)
    {
        var header = new byte[12];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(header.AsMemory(0, header.Length));
        if (read < 4) return null;

        // JPEG: FF D8 FF
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return ".jpg";
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47) return ".png";
        // GIF: 47 49 46 38
        if (header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38) return ".gif";
        // WebP: RIFF????WEBP (bytes 0-3 = RIFF, bytes 8-11 = WEBP)
        if (read >= 12 &&
            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
            return ".webp";

        return null; // Unknown / not an image
    }

    private IWebHostEnvironment _env =>
        HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
}