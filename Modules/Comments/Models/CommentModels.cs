using System.ComponentModel.DataAnnotations;

namespace HauntedVoiceUniverse.Modules.Comments.Models;

public class CommentResponse
{
    public Guid Id { get; set; }
    public Guid StoryId { get; set; }
    public Guid? EpisodeId { get; set; }
    public Guid? ParentCommentId { get; set; }
    public string Content { get; set; } = "";
    public int LikesCount { get; set; }
    public int RepliesCount { get; set; }
    public bool IsPinned { get; set; }
    public bool IsCreatorReply { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Author info
    public Guid AuthorId { get; set; }
    public string AuthorUsername { get; set; } = "";
    public string? AuthorDisplayName { get; set; }
    public string? AuthorAvatarUrl { get; set; }
    public bool AuthorIsCreator { get; set; }

    // Viewer context
    public bool IsLikedByMe { get; set; }
    public bool IsMyComment { get; set; }
}

public class CreateCommentRequest
{
    [Required]
    public Guid StoryId { get; set; }
    public Guid? EpisodeId { get; set; }
    public Guid? ParentCommentId { get; set; }

    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public string Content { get; set; } = "";
}

public class ReportCommentRequest
{
    [Required]
    public string Reason { get; set; } = "";
    public string? CustomReason { get; set; }
}
