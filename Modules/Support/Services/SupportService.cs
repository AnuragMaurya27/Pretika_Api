using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Infrastructure.Database;
using HauntedVoiceUniverse.Modules.Support.Models;
using Npgsql;

namespace HauntedVoiceUniverse.Modules.Support.Services;

public interface ISupportService
{
    Task<List<SupportCategoryResponse>> GetCategoriesAsync();
    Task<(bool Success, string Message, TicketResponse? Data)> CreateTicketAsync(Guid userId, CreateTicketRequest req);
    Task<PagedResult<TicketResponse>> GetMyTicketsAsync(Guid userId, string? status, int page, int pageSize);
    Task<TicketResponse?> GetTicketAsync(Guid userId, Guid ticketId);
    Task<List<TicketMessageResponse>> GetTicketMessagesAsync(Guid userId, Guid ticketId);
    Task<(bool Success, string Message, TicketMessageResponse? Data)> AddMessageAsync(Guid userId, Guid ticketId, AddTicketMessageRequest req);
    Task<(bool Success, string Message)> CloseTicketAsync(Guid userId, Guid ticketId);
    Task<(bool Success, string Message)> RateTicketAsync(Guid userId, Guid ticketId, RateTicketRequest req);
}

public class SupportService : ISupportService
{
    private readonly IDbConnectionFactory _db;

    public SupportService(IDbConnectionFactory db)
    {
        _db = db;
    }

    private static TicketResponse MapTicket(NpgsqlDataReader r)
    {
        return new TicketResponse
        {
            Id = DbHelper.GetGuid(r, "id"),
            TicketNumber = DbHelper.GetString(r, "ticket_number"),
            UserId = DbHelper.GetGuid(r, "user_id"),
            Username = DbHelper.GetString(r, "username"),
            CategoryId = r.IsDBNull(r.GetOrdinal("category_id")) ? null : DbHelper.GetGuid(r, "category_id"),
            CategoryName = DbHelper.GetStringOrNull(r, "category_name"),
            Subject = DbHelper.GetString(r, "subject"),
            Description = DbHelper.GetString(r, "description"),
            Status = DbHelper.GetString(r, "status"),
            Priority = DbHelper.GetString(r, "priority"),
            AssignedTo = r.IsDBNull(r.GetOrdinal("assigned_to")) ? null : DbHelper.GetGuid(r, "assigned_to"),
            AssignedToUsername = DbHelper.GetStringOrNull(r, "assigned_to_username"),
            SatisfactionRating = r.IsDBNull(r.GetOrdinal("satisfaction_rating")) ? null : DbHelper.GetInt(r, "satisfaction_rating"),
            SatisfactionFeedback = DbHelper.GetStringOrNull(r, "satisfaction_feedback"),
            CreatedAt = DbHelper.GetDateTime(r, "created_at"),
            UpdatedAt = DbHelper.GetDateTime(r, "updated_at"),
            ResolvedAt = DbHelper.GetDateTimeOrNull(r, "resolved_at"),
            MessageCount = DbHelper.GetInt(r, "message_count")
        };
    }

    public async Task<List<SupportCategoryResponse>> GetCategoriesAsync()
    {
        using var conn = await _db.CreateConnectionAsync();
        return await DbHelper.ExecuteReaderAsync(conn,
            "SELECT id, name, description, display_order FROM support_categories WHERE is_active = TRUE ORDER BY display_order",
            r => new SupportCategoryResponse
            {
                Id = DbHelper.GetGuid(r, "id"),
                Name = DbHelper.GetString(r, "name"),
                Description = DbHelper.GetStringOrNull(r, "description"),
                DisplayOrder = DbHelper.GetInt(r, "display_order")
            });
    }

    private static string GenerateTicketNumber()
    {
        var year = DateTime.UtcNow.Year;
        var random = new Random();
        return $"HVU-{year}-{random.Next(10000, 99999)}";
    }

    public async Task<(bool Success, string Message, TicketResponse? Data)> CreateTicketAsync(Guid userId, CreateTicketRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();

        // Limit open tickets per user
        var openCount = await DbHelper.ExecuteScalarAsync<long>(conn,
            "SELECT COUNT(*) FROM support_tickets WHERE user_id = @uid AND status NOT IN ('resolved', 'closed')",
            new Dictionary<string, object?> { ["uid"] = userId });
        if (openCount >= 5) return (false, "Aapke 5 open tickets pehle se hain. Koi resolve hone ke baad naya ticket banayein.", null);

        var ticketId = Guid.NewGuid();
        var ticketNumber = GenerateTicketNumber();

        await DbHelper.ExecuteNonQueryAsync(conn,
            @"INSERT INTO support_tickets (id, ticket_number, user_id, category_id, subject, description)
              VALUES (@id, @num, @uid, @catId, @subject, @desc)",
            new Dictionary<string, object?>
            {
                ["id"] = ticketId,
                ["num"] = ticketNumber,
                ["uid"] = userId,
                ["catId"] = (object?)req.CategoryId ?? DBNull.Value,
                ["subject"] = req.Subject,
                ["desc"] = req.Description
            });

        // Auto add first message (description as first message)
        await DbHelper.ExecuteNonQueryAsync(conn,
            "INSERT INTO ticket_messages (ticket_id, sender_id, message) VALUES (@tid, @uid, @msg)",
            new Dictionary<string, object?> { ["tid"] = ticketId, ["uid"] = userId, ["msg"] = req.Description });

        var ticket = await GetTicketAsync(userId, ticketId);
        return (true, $"Ticket {ticketNumber} create ho gaya. Humari team jald respond karegi.", ticket);
    }

    public async Task<PagedResult<TicketResponse>> GetMyTicketsAsync(Guid userId, string? status, int page, int pageSize)
    {
        using var conn = await _db.CreateConnectionAsync();
        pageSize = Math.Min(pageSize, 50);
        int offset = (page - 1) * pageSize;

        var where = "st.user_id = @uid";
        var parameters = new Dictionary<string, object?> { ["uid"] = userId };

        if (!string.IsNullOrEmpty(status))
        {
            where += " AND st.status = @status::ticket_status";
            parameters["status"] = status;
        }

        var total = await DbHelper.ExecuteScalarAsync<long>(conn,
            $"SELECT COUNT(*) FROM support_tickets st WHERE {where}", parameters);

        parameters["limit"] = pageSize;
        parameters["offset"] = offset;

        var sql = $@"
            SELECT st.*, u.username, sc.name as category_name,
                   a.username as assigned_to_username,
                   (SELECT COUNT(*) FROM ticket_messages tm WHERE tm.ticket_id = st.id) as message_count
            FROM support_tickets st
            JOIN users u ON u.id = st.user_id
            LEFT JOIN support_categories sc ON sc.id = st.category_id
            LEFT JOIN users a ON a.id = st.assigned_to
            WHERE {where}
            ORDER BY st.created_at DESC
            LIMIT @limit OFFSET @offset";

        var items = await DbHelper.ExecuteReaderAsync(conn, sql, MapTicket, parameters);
        return PagedResult<TicketResponse>.Create(items, (int)total, page, pageSize);
    }

    public async Task<TicketResponse?> GetTicketAsync(Guid userId, Guid ticketId)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await DbHelper.ExecuteReaderFirstAsync(conn,
            @"SELECT st.*, u.username, sc.name as category_name,
                     a.username as assigned_to_username,
                     (SELECT COUNT(*) FROM ticket_messages tm WHERE tm.ticket_id = st.id) as message_count
              FROM support_tickets st
              JOIN users u ON u.id = st.user_id
              LEFT JOIN support_categories sc ON sc.id = st.category_id
              LEFT JOIN users a ON a.id = st.assigned_to
              WHERE st.id = @id AND st.user_id = @uid",
            MapTicket,
            new Dictionary<string, object?> { ["id"] = ticketId, ["uid"] = userId });
    }

    public async Task<List<TicketMessageResponse>> GetTicketMessagesAsync(Guid userId, Guid ticketId)
    {
        using var conn = await _db.CreateConnectionAsync();

        // Verify ticket belongs to user
        var owns = await DbHelper.ExecuteScalarAsync<bool>(conn,
            "SELECT EXISTS(SELECT 1 FROM support_tickets WHERE id = @id AND user_id = @uid)",
            new Dictionary<string, object?> { ["id"] = ticketId, ["uid"] = userId });
        if (!owns) return new List<TicketMessageResponse>();

        return await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT tm.id, tm.ticket_id, tm.sender_id, tm.message, tm.is_internal_note, tm.created_at,
                     u.username as sender_username, u.avatar_url as sender_avatar_url,
                     CASE WHEN u.role != 'reader' AND u.role != 'creator' THEN TRUE ELSE FALSE END as is_support
              FROM ticket_messages tm
              JOIN users u ON u.id = tm.sender_id
              WHERE tm.ticket_id = @tid AND tm.is_internal_note = FALSE
              ORDER BY tm.created_at ASC",
            r => new TicketMessageResponse
            {
                Id = DbHelper.GetGuid(r, "id"),
                TicketId = DbHelper.GetGuid(r, "ticket_id"),
                SenderId = DbHelper.GetGuid(r, "sender_id"),
                SenderUsername = DbHelper.GetString(r, "sender_username"),
                SenderAvatarUrl = DbHelper.GetStringOrNull(r, "sender_avatar_url"),
                IsSupport = DbHelper.GetBool(r, "is_support"),
                Message = DbHelper.GetString(r, "message"),
                CreatedAt = DbHelper.GetDateTime(r, "created_at")
            },
            new Dictionary<string, object?> { ["tid"] = ticketId });
    }

    public async Task<(bool Success, string Message, TicketMessageResponse? Data)> AddMessageAsync(Guid userId, Guid ticketId, AddTicketMessageRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();

        var ticket = await DbHelper.ExecuteReaderFirstAsync(conn,
            "SELECT id, status FROM support_tickets WHERE id = @id AND user_id = @uid",
            r => new { Id = DbHelper.GetGuid(r, "id"), Status = DbHelper.GetString(r, "status") },
            new Dictionary<string, object?> { ["id"] = ticketId, ["uid"] = userId });

        if (ticket == null) return (false, "Ticket nahi mila", null);
        if (ticket.Status == "closed") return (false, "Closed ticket mein message nahi bhej sakte", null);

        var msgId = Guid.NewGuid();
        await DbHelper.ExecuteNonQueryAsync(conn,
            "INSERT INTO ticket_messages (id, ticket_id, sender_id, message) VALUES (@id, @tid, @uid, @msg)",
            new Dictionary<string, object?> { ["id"] = msgId, ["tid"] = ticketId, ["uid"] = userId, ["msg"] = req.Message });

        // Reopen if was waiting_user
        if (ticket.Status == "waiting_user")
        {
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE support_tickets SET status = 'in_progress', updated_at = NOW() WHERE id = @id",
                new Dictionary<string, object?> { ["id"] = ticketId });
        }
        else
        {
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE support_tickets SET updated_at = NOW() WHERE id = @id",
                new Dictionary<string, object?> { ["id"] = ticketId });
        }

        var username = await DbHelper.ExecuteScalarAsync<string>(conn,
            "SELECT username FROM users WHERE id = @uid",
            new Dictionary<string, object?> { ["uid"] = userId });

        return (true, "Message bhej diya", new TicketMessageResponse
        {
            Id = msgId,
            TicketId = ticketId,
            SenderId = userId,
            SenderUsername = username ?? "",
            IsSupport = false,
            Message = req.Message,
            CreatedAt = DateTime.UtcNow
        });
    }

    public async Task<(bool Success, string Message)> CloseTicketAsync(Guid userId, Guid ticketId)
    {
        using var conn = await _db.CreateConnectionAsync();
        var rows = await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE support_tickets SET status = 'closed', updated_at = NOW() WHERE id = @id AND user_id = @uid AND status != 'closed'",
            new Dictionary<string, object?> { ["id"] = ticketId, ["uid"] = userId });
        return rows > 0 ? (true, "Ticket close ho gaya") : (false, "Ticket nahi mila ya pehle se closed hai");
    }

    public async Task<(bool Success, string Message)> RateTicketAsync(Guid userId, Guid ticketId, RateTicketRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();
        var rows = await DbHelper.ExecuteNonQueryAsync(conn,
            @"UPDATE support_tickets SET satisfaction_rating = @rating, satisfaction_feedback = @feedback, updated_at = NOW()
              WHERE id = @id AND user_id = @uid AND status IN ('resolved', 'closed')",
            new Dictionary<string, object?>
            {
                ["id"] = ticketId,
                ["uid"] = userId,
                ["rating"] = req.Rating,
                ["feedback"] = (object?)req.Feedback ?? DBNull.Value
            });
        return rows > 0 ? (true, "Rating de di, shukriya!") : (false, "Ticket nahi mila ya abhi rate nahi kar sakte");
    }
}
