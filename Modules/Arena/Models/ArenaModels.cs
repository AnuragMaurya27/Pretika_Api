using System.ComponentModel.DataAnnotations;

namespace HauntedVoiceUniverse.Modules.Arena.Models;

// ═══════════════════════════════════════════════════════════════════════════
//  DARR ARENA — All Request / Response DTOs
// ═══════════════════════════════════════════════════════════════════════════

// ─── ADMIN REQUEST MODELS ────────────────────────────────────────────────────

public class CreateEventRequest
{
    [Required, StringLength(200, MinimumLength = 5)]
    public string Title { get; set; } = "";

    [Required, StringLength(2000, MinimumLength = 20)]
    public string Description { get; set; } = "";

    [Required, StringLength(1000, MinimumLength = 10)]
    public string Topic { get; set; } = "";

    [Required]
    public string StoryType { get; set; } = "short";   // short | long

    [Required, Range(100, 50000)]
    public int MinWordLimit { get; set; } = 500;

    [Required, Range(200, 100000)]
    public int MaxWordLimit { get; set; } = 2000;

    [Required, Range(0, 10000)]
    public int EntryFeeCoins { get; set; } = 50;

    [Range(0, 1000)]
    public int HallReadingCostCoins { get; set; } = 10;

    [Required, Range(1, 720)]
    public int WritingPhaseHours { get; set; } = 48;

    [Required, Range(1, 720)]
    public int ReviewPhaseHours { get; set; } = 48;

    [Required, Range(3, 10000)]
    public int MinParticipantsThreshold { get; set; } = 10;

    // Optional: admin can schedule a future start time
    public DateTime? WritingPhaseStartsAt { get; set; }
}

public class UpdateEventRequest
{
    [StringLength(200, MinimumLength = 5)]
    public string? Title { get; set; }

    [StringLength(2000, MinimumLength = 20)]
    public string? Description { get; set; }

    [StringLength(1000, MinimumLength = 10)]
    public string? Topic { get; set; }

    [Range(100, 50000)]
    public int? MinWordLimit { get; set; }

    [Range(200, 100000)]
    public int? MaxWordLimit { get; set; }

    [Range(0, 10000)]
    public int? EntryFeeCoins { get; set; }

    [Range(0, 1000)]
    public int? HallReadingCostCoins { get; set; }

    [Range(1, 720)]
    public int? WritingPhaseHours { get; set; }

    [Range(1, 720)]
    public int? ReviewPhaseHours { get; set; }

    [Range(3, 10000)]
    public int? MinParticipantsThreshold { get; set; }

    public DateTime? WritingPhaseStartsAt { get; set; }
}

public class CancelEventRequest
{
    [StringLength(500)]
    public string? Reason { get; set; }
}

// ─── PARTICIPANT REQUEST MODELS ───────────────────────────────────────────────

public class SaveDraftRequest
{
    [StringLength(300)]
    public string Title { get; set; } = "";

    public string Content { get; set; } = "";   // Quill delta or plain text

    public int WordCount { get; set; } = 0;
}

public class QuestionDto
{
    [Required, StringLength(1000, MinimumLength = 10)]
    public string QuestionText { get; set; } = "";

    [Required, StringLength(500, MinimumLength = 1)]
    public string OptionA { get; set; } = "";

    [Required, StringLength(500, MinimumLength = 1)]
    public string OptionB { get; set; } = "";

    [Required, StringLength(500, MinimumLength = 1)]
    public string OptionC { get; set; } = "";

    [Required, StringLength(500, MinimumLength = 1)]
    public string OptionD { get; set; } = "";

    [Required]
    [RegularExpression("^[ABCD]$", ErrorMessage = "Correct option must be A, B, C, or D")]
    public string CorrectOption { get; set; } = "A";
}

public class SubmitQuestionsRequest
{
    [Required]
    [MinLength(3), MaxLength(3)]
    public List<QuestionDto> Questions { get; set; } = new();
}

// ─── REVIEW REQUEST MODELS ────────────────────────────────────────────────────

public class AnswerQuestionsRequest
{
    [Required]
    [RegularExpression("^[ABCD]$")]
    public string AnswerQ1 { get; set; } = "";

    [Required]
    [RegularExpression("^[ABCD]$")]
    public string AnswerQ2 { get; set; } = "";

    [Required]
    [RegularExpression("^[ABCD]$")]
    public string AnswerQ3 { get; set; } = "";
}

public class SubmitRatingRequest
{
    [Required, Range(1, 10)]
    public int Rating { get; set; }

    [StringLength(500)]
    public string? Comment { get; set; }
}

// ─── RESPONSE MODELS ─────────────────────────────────────────────────────────

public class ArenaEventResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Topic { get; set; } = "";
    public string StoryType { get; set; } = "";
    public int MinWordLimit { get; set; }
    public int MaxWordLimit { get; set; }
    public int EntryFeeCoins { get; set; }
    public int HallReadingCostCoins { get; set; }
    public int WritingPhaseHours { get; set; }
    public int ReviewPhaseHours { get; set; }
    public int MinParticipantsThreshold { get; set; }
    public string Status { get; set; } = "";
    public long OriginalPrizePool { get; set; }
    public long PrizePotLive { get; set; }  // entry_fee * current participants (pre-lock)
    public int TotalParticipants { get; set; }
    public int TotalSubmitted { get; set; }
    public DateTime? WritingPhaseStartsAt { get; set; }
    public DateTime? WritingPhaseEndsAt { get; set; }
    public DateTime? ReviewPhaseEndsAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CancellationReason { get; set; }
    public string CreatedByUsername { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    // Viewer context
    public bool? HasJoined { get; set; }
    public bool? HasSubmitted { get; set; }
}

public class ArenaEventStatsResponse
{
    public Guid EventId { get; set; }
    public int TotalParticipants { get; set; }
    public int TotalSubmitted { get; set; }
    public int TotalDisqualified { get; set; }
    public int TotalReviewsCompleted { get; set; }
    public int TotalReviewsAssigned { get; set; }
    public int TotalReviewsDefaulted { get; set; }
    public long TotalCoinsCollected { get; set; }
    public long ForfeitPoolBalance { get; set; }
}

public class ArenaStoryResponse
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public int WordCount { get; set; }
    public string Status { get; set; } = "";
    public bool QuestionsSubmitted { get; set; }
    public DateTime? DraftSavedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public List<QuestionPreviewDto>? Questions { get; set; }  // null until submitted
}

public class QuestionPreviewDto
{
    public int Order { get; set; }
    public string QuestionText { get; set; } = "";
    public string OptionA { get; set; } = "";
    public string OptionB { get; set; } = "";
    public string OptionC { get; set; } = "";
    public string OptionD { get; set; } = "";
    // CorrectOption is NEVER included in responses sent to reviewers
}

public class SubmitStatusResponse
{
    public bool WritingPhaseEnded { get; set; }
    public bool CanSubmit { get; set; }
    public bool HasSubmitted { get; set; }
    public bool QuestionsSubmitted { get; set; }
    public DateTime? WritingPhaseEndsAt { get; set; }
    public TimeSpan? TimeRemaining { get; set; }
    public int WordCount { get; set; }
    public bool MeetsMinWordLimit { get; set; }
}

public class ArenaAssignmentResponse
{
    public Guid AssignmentId { get; set; }
    public Guid EventId { get; set; }
    public string EventTitle { get; set; } = "";
    public Guid StoryId { get; set; }
    public string Status { get; set; } = "";
    public bool ReadTimeVerified { get; set; }
    public bool ScrollVerified { get; set; }
    public bool QuestionsPassed { get; set; }
    public int WrongAttempts { get; set; }
    public DateTime? ReviewPhaseEndsAt { get; set; }
    public TimeSpan? TimeRemaining { get; set; }
    public bool IsExtraReview { get; set; }
    public int ExtraCoinsOffered { get; set; }
}

public class ArenaStoryReviewResponse
{
    public Guid AssignmentId { get; set; }
    public Guid StoryId { get; set; }
    public string StoryTitle { get; set; } = "";
    public string Content { get; set; } = "";          // Full story content
    public int WordCount { get; set; }
    public int MinReadTimeSeconds { get; set; }         // word_count / 200 * 60
    public bool ReadTimeVerified { get; set; }
    public bool ScrollVerified { get; set; }
    public bool QuestionsPassed { get; set; }
    public int WrongAttempts { get; set; }
    // Author info: NEVER included — blind review
    public List<QuestionPreviewDto>? Questions { get; set; } // Only after read+scroll verified
}

public class AnswerQuestionsResult
{
    public bool AllCorrect { get; set; }
    public int CorrectCount { get; set; }
    public int WrongAttempts { get; set; }      // Running total
    public bool Disqualified { get; set; }
    public bool MustReReadStory { get; set; }   // Reset at 4 wrong
    public string? Message { get; set; }
}

public class ArenaWinnerResponse
{
    public int Rank { get; set; }
    public Guid UserId { get; set; }
    public string Username { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public Guid StoryId { get; set; }
    public string StoryTitle { get; set; } = "";
    public decimal AverageRating { get; set; }
    public int TotalReviews { get; set; }
    public long CoinsWon { get; set; }
}

public class ArenaLeaderboardEntry
{
    public int Position { get; set; }
    public Guid UserId { get; set; }
    public string Username { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string StoryTitle { get; set; } = "";
    public decimal AverageRating { get; set; }
    public int TotalReviews { get; set; }
    public long CoinsWon { get; set; }  // 0 for non-winners
    public string Status { get; set; } = "";  // active / disqualified / defaulted / void
}

public class ArenaResultsResponse
{
    public Guid EventId { get; set; }
    public string EventTitle { get; set; } = "";
    public string Topic { get; set; } = "";
    public int TotalParticipants { get; set; }
    public long TotalPrizePool { get; set; }
    public List<ArenaWinnerResponse> Winners { get; set; } = new();
    public List<ArenaLeaderboardEntry> Leaderboard { get; set; } = new();
    public DateTime? AnnouncedAt { get; set; }
}

public class MyArenaResultResponse
{
    public Guid EventId { get; set; }
    public string EventTitle { get; set; } = "";
    public int Position { get; set; }
    public int TotalParticipants { get; set; }
    public decimal AverageRating { get; set; }
    public int TotalReviews { get; set; }
    public decimal? BestRatingReceived { get; set; }
    public decimal? WorstRatingReceived { get; set; }
    public long CoinsWon { get; set; }
    public string ParticipantStatus { get; set; } = "";
    public string StoryStatus { get; set; } = "";
    public string? StoryTitle { get; set; }
}

public class HallOfChampionsResponse
{
    public Guid EventId { get; set; }
    public string EventTitle { get; set; } = "";
    public string Topic { get; set; } = "";
    public string Status { get; set; } = "";    // completed | cancelled
    public int TotalParticipants { get; set; }
    public long TotalCoinPool { get; set; }
    public int HallReadingCostCoins { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CancellationReason { get; set; }
    public List<ArenaWinnerResponse> Winners { get; set; } = new();
}

public class MostFearedStoryResponse
{
    public Guid EventId { get; set; }
    public string EventTitle { get; set; } = "";
    public Guid StoryId { get; set; }
    public string StoryTitle { get; set; } = "";
    public string AuthorUsername { get; set; } = "";
    public string? AuthorAvatarUrl { get; set; }
    public decimal AverageRating { get; set; }
    public int TotalReviews { get; set; }
    public int HallReadingCostCoins { get; set; }
    public DateTime AchievedAt { get; set; }
}

public class ArenaBadgeResponse
{
    public Guid BadgeId { get; set; }
    public Guid EventId { get; set; }
    public string EventTitle { get; set; } = "";
    public Guid StoryId { get; set; }
    public string BadgeType { get; set; } = "";   // champion | runner_up | second_runner_up
    public int Rank { get; set; }
    public long CoinsWon { get; set; }
    public DateTime AwardedAt { get; set; }
}

// Internal helper classes (not exposed as API responses)

internal class StoryParticipantInfo
{
    public Guid StoryId { get; set; }
    public Guid AuthorId { get; set; }
}

internal class ParticipantInfo
{
    public Guid UserId { get; set; }
    public int ReviewsAssigned { get; set; }
}
