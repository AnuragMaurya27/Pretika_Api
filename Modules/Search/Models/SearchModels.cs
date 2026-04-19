namespace HauntedVoiceUniverse.Modules.Search.Models;

// ─── Search Request ────────────────────────────────────────────────────────────

public class UnifiedSearchRequest
{
    /// <summary>Search query string</summary>
    public string Q { get; set; } = "";

    /// <summary>all | stories | users</summary>
    public string SearchType { get; set; } = "all";

    // Story filters
    public string? SortBy { get; set; }        // trending | most_viewed | most_liked | top_rated | recently_updated | latest
    public string? Language { get; set; }       // hindi | english | hinglish
    public string? Category { get; set; }       // category slug
    public string? StoryType { get; set; }      // single | series
    public string? AgeRating { get; set; }      // all | teen | adult
    public string? DateFrom { get; set; }       // ISO date
    public string? DateTo { get; set; }         // ISO date

    // Pagination
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 15;
}

// ─── Search Creator Result ────────────────────────────────────────────────────

public class SearchUserResult
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsCreator { get; set; }
    public bool IsVerifiedCreator { get; set; }
    public string ReaderFearRank { get; set; } = "";
    public string? CreatorFearRank { get; set; }
    public int TotalFollowers { get; set; }
    public int TotalStories { get; set; }
    public string? Bio { get; set; }
    public bool IsFollowing { get; set; }
    public bool IsFollowedByThem { get; set; }
}

// ─── Unified Search Response ──────────────────────────────────────────────────

public class UnifiedSearchResponse
{
    public string Query { get; set; } = "";
    public string SearchType { get; set; } = "all";

    // Paginated story results
    public SearchStoriesPage? Stories { get; set; }

    // User results (non-paged, top 10)
    public List<SearchUserResult> Users { get; set; } = new();
    public int TotalUsers { get; set; }
}

public class SearchStoriesPage
{
    public List<HauntedVoiceUniverse.Modules.Stories.Models.StoryResponse> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
    public bool HasNextPage => Page < TotalPages;
}

// ─── Report (User-Facing) ─────────────────────────────────────────────────────

public class CreateReportRequest
{
    /// <summary>story | episode | user | comment</summary>
    public string EntityType { get; set; } = "";

    public Guid EntityId { get; set; }

    /// <summary>inappropriate | spam | hate_speech | adult_content | copyright | misinformation | other</summary>
    public string Reason { get; set; } = "";

    /// <summary>Optional additional description (max 500 chars)</summary>
    public string? Description { get; set; }
}
