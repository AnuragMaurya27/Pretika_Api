using System.ComponentModel.DataAnnotations;

namespace HauntedVoiceUniverse.Modules.Stories.Models;

// ─── Story Response DTOs ──────────────────────────────────────────────────────

public class StoryResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Summary { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string StoryType { get; set; } = "single";
    public string Language { get; set; } = "hindi";
    public string AgeRating { get; set; } = "all";
    public string Status { get; set; } = "draft";

    // Creator info
    public Guid CreatorId { get; set; }
    public string CreatorUsername { get; set; } = "";
    public string? CreatorDisplayName { get; set; }
    public string? CreatorAvatarUrl { get; set; }
    public bool IsVerifiedCreator { get; set; }

    // Category
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }

    // Collection
    public Guid? CollectionId { get; set; }
    public string? CollectionName { get; set; }

    // Tags
    public List<string> Tags { get; set; } = new();

    // Metrics
    public int TotalEpisodes { get; set; }
    public long TotalViews { get; set; }
    public long TotalLikes { get; set; }
    public long TotalComments { get; set; }
    public long TotalBookmarks { get; set; }
    public decimal EngagementScore { get; set; }
    public bool IsEditorPick { get; set; }

    // Viewer context
    public bool? IsLiked { get; set; }
    public bool? IsBookmarked { get; set; }

    // Ratings
    public double AverageRating { get; set; }
    public int RatingCount { get; set; }
    public int? MyRating { get; set; }

    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class StoryDetailResponse : StoryResponse
{
    public List<EpisodeResponse> Episodes { get; set; } = new();
}

public class RateStoryRequest
{
    [Range(1, 5)]
    public int Rating { get; set; }
}

public class EpisodeResponse
{
    public Guid Id { get; set; }
    public Guid StoryId { get; set; }
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public int EpisodeNumber { get; set; }
    public string AccessType { get; set; } = "free";
    public int UnlockCoinCost { get; set; }
    public string Status { get; set; } = "draft";
    public int WordCount { get; set; }
    public int EstimatedReadTimeSeconds { get; set; }

    // Metrics
    public long TotalViews { get; set; }
    public long TotalLikes { get; set; }
    public long TotalComments { get; set; }

    // Content - sirf unlocked ya free episode mein milega
    public string? Content { get; set; }
    public bool IsUnlocked { get; set; }
    public bool? IsLiked { get; set; }

    // Viewer's existing rating for this episode's story (1-5, null if not rated)
    public int? UserRating { get; set; }

    public DateTime? PublishedAt { get; set; }
    // BUG#M3-5 FIX: Return scheduled_publish_at so Flutter editor can restore the saved schedule
    public DateTime? ScheduledPublishAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CategoryResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Slug { get; set; }
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public int TotalStories { get; set; }
}

// ─── Request DTOs ─────────────────────────────────────────────────────────────

public class CreateCategoryRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = "";
    [MaxLength(500)]
    public string? Description { get; set; }
    [MaxLength(500)]
    public string? IconUrl { get; set; }
}

public class CreateStoryRequest
{
    [Required, MaxLength(255)]
    public string Title { get; set; } = "";

    [MaxLength(1000)]
    public string? Summary { get; set; }

    public Guid? CategoryId { get; set; }

    public string StoryType { get; set; } = "single"; // single / series

    public string Language { get; set; } = "hindi"; // hindi / hinglish / english

    public string AgeRating { get; set; } = "all"; // all / 13+ / 16+ / 18+

    public List<string> Tags { get; set; } = new();

    public string? ThumbnailUrl { get; set; }
}

public class UpdateStoryRequest
{
    [MaxLength(255)]
    public string? Title { get; set; }

    [MaxLength(1000)]
    public string? Summary { get; set; }

    public Guid? CategoryId { get; set; }
    public string? Language { get; set; }
    public string? AgeRating { get; set; }
    public List<string>? Tags { get; set; }
    public string? ThumbnailUrl { get; set; }
}

public class CreateEpisodeRequest
{
    [Required, MaxLength(255)]
    public string Title { get; set; } = "";

    [Required]
    public string Content { get; set; } = "";

    public string AccessType { get; set; } = "free"; // free / premium

    public int UnlockCoinCost { get; set; } = 0;

    public DateTime? ScheduledPublishAt { get; set; }
}

public class UpdateEpisodeRequest
{
    [MaxLength(255)]
    public string? Title { get; set; }
    public string? Content { get; set; }
    public string? AccessType { get; set; }
    public int? UnlockCoinCost { get; set; }
    public DateTime? ScheduledPublishAt { get; set; }
}

public class StoryFilterRequest
{
    public string? Category { get; set; }       // category slug
    public string? Language { get; set; }       // hindi / hinglish / english
    public string? StoryType { get; set; }      // single / series
    public string? AgeRating { get; set; }      // all / 13+ / 16+ / 18+
    public string? Search { get; set; }         // searches: title, summary, creator username/display, tags
    public string? CreatorUsername { get; set; }// filter by creator username (exact/partial)
    public string? DateFrom { get; set; }       // published_at >= DateFrom (ISO yyyy-MM-dd)
    public string? DateTo { get; set; }         // published_at <= DateTo   (ISO yyyy-MM-dd)
    public string SortBy { get; set; } = "latest"; // latest / trending / most_viewed / most_liked / recently_updated / top_rated
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

// ─── Collection DTOs ──────────────────────────────────────────────────────────

public class CollectionResponse
{
    public Guid Id { get; set; }
    public Guid CreatorId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? CoverUrl { get; set; }
    public bool IsPublic { get; set; }
    public int TotalStories { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateCollectionRequest
{
    [Required, MaxLength(255)]
    public string Name { get; set; } = "";

    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? CoverUrl { get; set; }

    public bool IsPublic { get; set; } = true;
}

public class UpdateCollectionRequest
{
    [MaxLength(255)]
    public string? Name { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? CoverUrl { get; set; }

    public bool? IsPublic { get; set; }
}