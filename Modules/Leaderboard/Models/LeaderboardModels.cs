using System.ComponentModel.DataAnnotations;

namespace HauntedVoiceUniverse.Modules.Leaderboard.Models;

public class LeaderboardEntryResponse
{
    public Guid Id { get; set; }
    public int RankPosition { get; set; }
    public decimal Score { get; set; }
    public string EntityType { get; set; } = ""; // 'story' or 'creator'
    public Guid EntityId { get; set; }

    // Story fields (when entity_type = 'story')
    public string? StoryTitle { get; set; }
    public string? StorySlug { get; set; }
    public string? StoryThumbnailUrl { get; set; }
    public long? StoryTotalViews { get; set; }
    public long? StoryTotalLikes { get; set; }

    // Creator fields (when entity_type = 'creator')
    public string? CreatorUsername { get; set; }
    public string? CreatorDisplayName { get; set; }
    public string? CreatorAvatarUrl { get; set; }
    public bool? CreatorIsVerified { get; set; }
    public string? CreatorRank { get; set; }

    public int RewardCoins { get; set; }
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }
}

public class CompetitionResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Rules { get; set; }
    public string? BannerUrl { get; set; }
    public string? Theme { get; set; }
    public long PrizePoolCoins { get; set; }
    public DateTime SubmissionStart { get; set; }
    public DateTime SubmissionEnd { get; set; }
    public DateTime? VotingStart { get; set; }
    public DateTime? VotingEnd { get; set; }
    public DateTime? ResultsAnnouncedAt { get; set; }
    public int MaxEntriesPerUser { get; set; }
    public int? MinWordCount { get; set; }
    public int? MaxWordCount { get; set; }
    public bool IsActive { get; set; }
    public bool IsFeatured { get; set; }
    public int TotalEntries { get; set; }
    public int TotalVotes { get; set; }
    public string Status { get; set; } = ""; // upcoming, submission_open, voting, ended
    public bool HasEntered { get; set; }
}

public class CompetitionEntryResponse
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid UserId { get; set; }
    public string Username { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public Guid StoryId { get; set; }
    public string StoryTitle { get; set; } = "";
    public string StorySlug { get; set; } = "";
    public string? StoryThumbnailUrl { get; set; }
    public int VoteCount { get; set; }
    public int? FinalRank { get; set; }
    public bool IsWinner { get; set; }
    public DateTime SubmittedAt { get; set; }
    public bool HasVoted { get; set; }
}

public class SubmitEntryRequest
{
    [Required]
    public Guid StoryId { get; set; }
}

public class VoteEntryRequest
{
    // Entry ID is in the URL
}
