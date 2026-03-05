using System.ComponentModel.DataAnnotations;

namespace HauntedVoiceUniverse.Modules.Support.Models;

public class SupportCategoryResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
}

public class TicketResponse
{
    public Guid Id { get; set; }
    public string TicketNumber { get; set; } = "";
    public Guid UserId { get; set; }
    public string Username { get; set; } = "";
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string Subject { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "";
    public string Priority { get; set; } = "";
    public Guid? AssignedTo { get; set; }
    public string? AssignedToUsername { get; set; }
    public int? SatisfactionRating { get; set; }
    public string? SatisfactionFeedback { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int MessageCount { get; set; }
}

public class TicketMessageResponse
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public Guid SenderId { get; set; }
    public string SenderUsername { get; set; } = "";
    public string? SenderAvatarUrl { get; set; }
    public bool IsSupport { get; set; }
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class CreateTicketRequest
{
    public Guid? CategoryId { get; set; }

    [Required]
    [StringLength(255, MinimumLength = 5)]
    public string Subject { get; set; } = "";

    [Required]
    [StringLength(5000, MinimumLength = 20)]
    public string Description { get; set; } = "";
}

public class AddTicketMessageRequest
{
    [Required]
    [StringLength(5000, MinimumLength = 1)]
    public string Message { get; set; } = "";
}

public class RateTicketRequest
{
    [Required]
    [Range(1, 5)]
    public int Rating { get; set; }

    [StringLength(500)]
    public string? Feedback { get; set; }
}
