using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Infrastructure.Database;
using HauntedVoiceUniverse.Modules.Arena.Models;
using HauntedVoiceUniverse.Modules.Notifications.Services;
using Npgsql;

namespace HauntedVoiceUniverse.Modules.Arena.Services;

// ═══════════════════════════════════════════════════════════════════════════
//  DARR ARENA — Service Interface
// ═══════════════════════════════════════════════════════════════════════════

public interface IArenaService
{
    // Admin
    Task<(bool Success, string Message, ArenaEventResponse? Data)> CreateEventAsync(Guid adminId, CreateEventRequest req);
    Task<(bool Success, string Message)> UpdateEventAsync(Guid adminId, Guid eventId, UpdateEventRequest req);
    Task<(bool Success, string Message)> CancelEventAsync(Guid adminId, Guid eventId, CancelEventRequest req);
    Task<PagedResult<ArenaEventResponse>> GetEventsAsync(Guid? viewerId, string? status, int page, int pageSize);
    Task<ArenaEventResponse?> GetEventByIdAsync(Guid eventId, Guid? viewerId);
    Task<ArenaEventStatsResponse?> GetEventStatsAsync(Guid eventId);

    // Participant
    Task<(bool Success, string Message)> JoinEventAsync(Guid userId, Guid eventId);
    Task<(bool Success, string Message)> SaveDraftAsync(Guid userId, Guid eventId, SaveDraftRequest req);
    Task<(bool Success, string Message)> SubmitStoryAsync(Guid userId, Guid eventId);
    Task<(bool Success, string Message)> SubmitQuestionsAsync(Guid userId, Guid eventId, SubmitQuestionsRequest req);
    Task<ArenaStoryResponse?> GetMyStoryAsync(Guid userId, Guid eventId);
    Task<SubmitStatusResponse?> GetSubmitStatusAsync(Guid userId, Guid eventId);

    // Review
    Task<List<ArenaAssignmentResponse>> GetMyAssignmentsAsync(Guid userId, Guid eventId);
    Task<ArenaStoryReviewResponse?> GetStoryForReviewAsync(Guid userId, Guid assignmentId);
    Task<(bool Success, string Message)> VerifyReadTimeAsync(Guid userId, Guid assignmentId);
    Task<(bool Success, string Message)> VerifyScrollAsync(Guid userId, Guid assignmentId);
    Task<(bool Success, string Message, AnswerQuestionsResult? Data)> AnswerQuestionsAsync(Guid userId, Guid assignmentId, AnswerQuestionsRequest req);
    Task<(bool Success, string Message)> SubmitRatingAsync(Guid userId, Guid assignmentId, SubmitRatingRequest req);

    // Results
    Task<ArenaResultsResponse?> GetEventResultsAsync(Guid eventId);
    Task<MyArenaResultResponse?> GetMyResultAsync(Guid userId, Guid eventId);
    Task<List<HallOfChampionsResponse>> GetHallOfChampionsAsync(int page, int pageSize);
    Task<MostFearedStoryResponse?> GetMostFearedStoryAsync(Guid eventId);

    // Profile
    Task<List<ArenaBadgeResponse>> GetUserBadgesAsync(Guid userId);

    // Background job hooks
    Task TransitionToReviewPhaseAsync(Guid eventId);
    Task FinalizeEventAsync(Guid eventId);
    Task RunForfeitDetectionAsync(Guid eventId);
}

// ═══════════════════════════════════════════════════════════════════════════
//  DARR ARENA — Service Implementation
// ═══════════════════════════════════════════════════════════════════════════

public class ArenaService : IArenaService
{
    private readonly IDbConnectionFactory _db;
    private readonly INotificationService _notifications;
    private readonly ILogger<ArenaService> _logger;

    // Revenue split constants
    private const decimal PlatformCutPercent  = 0.30m;
    private const decimal Rank1CutPercent     = 0.35m;
    private const decimal Rank2CutPercent     = 0.20m;
    private const decimal Rank3CutPercent     = 0.15m;

    // Anti-cheat escalation thresholds
    private const int WarnLevel1  = 1;
    private const int WarnLevel2  = 2;
    private const int WarnLevel3  = 3;
    private const int ReReadLevel = 4;
    private const int DisqualLevel = 5;

    // Reviews per submitted story
    private const int ReviewsPerStory = 5;

    // Extra coin incentive for forfeit-redistributed stories
    private const int ExtraCoinsPerForfeitReview = 20;

    public ArenaService(
        IDbConnectionFactory db,
        INotificationService notifications,
        ILogger<ArenaService> logger)
    {
        _db = db;
        _notifications = notifications;
        _logger = logger;
    }

    // ───────────────────────────────────────────────────────────────────────
    //  ADMIN — EVENT CRUD
    // ───────────────────────────────────────────────────────────────────────

    public async Task<(bool Success, string Message, ArenaEventResponse? Data)> CreateEventAsync(
        Guid adminId, CreateEventRequest req)
    {
        if (req.MinWordLimit >= req.MaxWordLimit)
            return (false, "Min word limit must be less than max word limit", null);

        var startsAt = req.WritingPhaseStartsAt ?? DateTime.UtcNow;
        if (startsAt < DateTime.UtcNow.AddMinutes(-1))
            return (false, "Writing phase start time cannot be in the past", null);

        await using var conn = await _db.CreateConnectionAsync();
        var eventId = Guid.NewGuid();
        var status  = startsAt <= DateTime.UtcNow.AddMinutes(5) ? "writing" : "upcoming";
        var writingEndsAt = startsAt.AddHours(req.WritingPhaseHours);

        await DbHelper.ExecuteNonQueryAsync(conn,
            @"INSERT INTO arena_events
              (id, title, description, topic, story_type,
               min_word_limit, max_word_limit, entry_fee_coins,
               hall_reading_cost_coins, writing_phase_hours, review_phase_hours,
               min_participants_threshold, status, writing_phase_starts_at,
               writing_phase_ends_at, created_by, created_at, updated_at)
              VALUES
              (@id, @title, @desc, @topic, @st,
               @minW, @maxW, @fee,
               @hall, @wph, @rph,
               @minP, @status::arena_event_status, @wStart,
               @wEnd, @admin, NOW(), NOW())",
            new Dictionary<string, object?>
            {
                ["id"]     = eventId,
                ["title"]  = req.Title,
                ["desc"]   = req.Description,
                ["topic"]  = req.Topic,
                ["st"]     = req.StoryType,
                ["minW"]   = req.MinWordLimit,
                ["maxW"]   = req.MaxWordLimit,
                ["fee"]    = req.EntryFeeCoins,
                ["hall"]   = req.HallReadingCostCoins,
                ["wph"]    = req.WritingPhaseHours,
                ["rph"]    = req.ReviewPhaseHours,
                ["minP"]   = req.MinParticipantsThreshold,
                ["status"] = status,
                ["wStart"] = startsAt,
                ["wEnd"]   = writingEndsAt,
                ["admin"]  = adminId,
            });

        var created = await GetEventByIdAsync(eventId, adminId);
        return (true, "Darr Arena event bana diya gaya!", created);
    }

    public async Task<(bool Success, string Message)> UpdateEventAsync(
        Guid adminId, Guid eventId, UpdateEventRequest req)
    {
        await using var conn = await _db.CreateConnectionAsync();

        var status = await DbHelper.ExecuteScalarAsync<string>(conn,
            "SELECT status FROM arena_events WHERE id = @id AND deleted_at IS NULL",
            new Dictionary<string, object?> { ["id"] = eventId });

        if (status == null) return (false, "Event nahi mila");
        if (status != "upcoming") return (false, "Sirf upcoming events update kiye ja sakte hain");

        var setClauses = new List<string> { "updated_at = NOW()" };
        var parameters = new Dictionary<string, object?> { ["id"] = eventId };

        if (req.Title != null)        { setClauses.Add("title = @title");               parameters["title"] = req.Title; }
        if (req.Description != null)  { setClauses.Add("description = @desc");          parameters["desc"] = req.Description; }
        if (req.Topic != null)        { setClauses.Add("topic = @topic");               parameters["topic"] = req.Topic; }
        if (req.MinWordLimit != null) { setClauses.Add("min_word_limit = @minW");       parameters["minW"] = req.MinWordLimit; }
        if (req.MaxWordLimit != null) { setClauses.Add("max_word_limit = @maxW");       parameters["maxW"] = req.MaxWordLimit; }
        if (req.EntryFeeCoins != null){ setClauses.Add("entry_fee_coins = @fee");       parameters["fee"] = req.EntryFeeCoins; }
        if (req.HallReadingCostCoins != null) { setClauses.Add("hall_reading_cost_coins = @hall"); parameters["hall"] = req.HallReadingCostCoins; }
        if (req.WritingPhaseHours != null){ setClauses.Add("writing_phase_hours = @wph"); parameters["wph"] = req.WritingPhaseHours; }
        if (req.ReviewPhaseHours != null) { setClauses.Add("review_phase_hours = @rph"); parameters["rph"] = req.ReviewPhaseHours; }
        if (req.MinParticipantsThreshold != null){ setClauses.Add("min_participants_threshold = @minP"); parameters["minP"] = req.MinParticipantsThreshold; }
        if (req.WritingPhaseStartsAt != null)
        {
            setClauses.Add("writing_phase_starts_at = @wStart");
            parameters["wStart"] = req.WritingPhaseStartsAt;
        }

        await DbHelper.ExecuteNonQueryAsync(conn,
            $"UPDATE arena_events SET {string.Join(", ", setClauses)} WHERE id = @id",
            parameters);

        return (true, "Event update ho gaya");
    }

    public async Task<(bool Success, string Message)> CancelEventAsync(
        Guid adminId, Guid eventId, CancelEventRequest req)
    {
        await using var conn = await _db.CreateConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            var ev = await DbHelper.ExecuteReaderFirstAsync(conn,
                @"SELECT id, status, entry_fee_coins FROM arena_events
                  WHERE id = @id AND deleted_at IS NULL FOR UPDATE",
                r => new
                {
                    Id      = DbHelper.GetGuid(r, "id"),
                    Status  = DbHelper.GetString(r, "status"),
                    Fee     = DbHelper.GetInt(r, "entry_fee_coins"),
                },
                new Dictionary<string, object?> { ["id"] = eventId }, tx);

            if (ev == null) return (false, "Event nahi mila");
            if (ev.Status is "completed" or "cancelled")
                return (false, "Yeh event already finalize ho chuka hai");

            // Refund entry fees to all joined participants
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"UPDATE wallets w
                  SET coin_balance = coin_balance + @fee,
                      total_earned = total_earned + @fee,
                      updated_at   = NOW()
                  FROM arena_participants ap
                  WHERE ap.event_id = @eid
                    AND ap.user_id  = w.user_id
                    AND ap.status NOT IN ('refunded')",
                new Dictionary<string, object?>
                {
                    ["fee"] = ev.Fee,
                    ["eid"] = eventId,
                }, tx);

            // Log refund transactions
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO coin_transactions (id, sender_id, receiver_id, amount, type, description, status, completed_at)
                  SELECT gen_random_uuid(), NULL, ap.user_id, @fee, 'arena_refund',
                         'Arena cancelled — entry fee refunded: ' || @reason,
                         'completed', NOW()
                  FROM arena_participants ap
                  WHERE ap.event_id = @eid AND ap.status NOT IN ('refunded')",
                new Dictionary<string, object?>
                {
                    ["fee"]    = ev.Fee,
                    ["eid"]    = eventId,
                    ["reason"] = req.Reason ?? "No reason provided",
                }, tx);

            // Update participant statuses
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE arena_participants SET status = 'refunded', updated_at = NOW() WHERE event_id = @eid",
                new Dictionary<string, object?> { ["eid"] = eventId }, tx);

            // Mark event cancelled
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"UPDATE arena_events
                  SET status = 'cancelled'::arena_event_status,
                      cancellation_reason = @reason,
                      updated_at = NOW()
                  WHERE id = @id",
                new Dictionary<string, object?>
                {
                    ["id"]     = eventId,
                    ["reason"] = req.Reason,
                }, tx);

            await tx.CommitAsync();

            // Notify all participants asynchronously (fire-and-forget per participant)
            _ = Task.Run(async () =>
            {
                await using var notifConn = await _db.CreateConnectionAsync();
                var participantIds = await DbHelper.ExecuteReaderAsync(notifConn,
                    "SELECT user_id FROM arena_participants WHERE event_id = @eid",
                    new Dictionary<string, object?> { ["eid"] = eventId });

                foreach (var row in participantIds)
                {
                    var uid = (Guid)row["user_id"]!;
                    await _notifications.CreateNotificationAsync(
                        uid, "arena_cancelled",
                        "Darr Arena Cancelled",
                        $"Event cancelled. Entry fee ({ev.Fee} coins) wapas aa gaye.",
                        actorId: adminId,
                        actionUrl: $"/arena/{eventId}");
                }
            });

            return (true, "Event cancel kar diya gaya. Sabka entry fee refund ho raha hai.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "CancelEvent failed for {EventId}", eventId);
            return (false, "Cancel karte waqt error aaya. Dobara koshish karein.");
        }
    }

    public async Task<PagedResult<ArenaEventResponse>> GetEventsAsync(
        Guid? viewerId, string? status, int page, int pageSize)
    {
        await using var conn = await _db.CreateConnectionAsync();
        pageSize = Math.Min(pageSize, 50);
        int offset = (page - 1) * pageSize;

        var where = "WHERE ae.deleted_at IS NULL";
        var parameters = new Dictionary<string, object?>();

        if (!string.IsNullOrEmpty(status))
        {
            where += " AND ae.status = @status::arena_event_status";
            parameters["status"] = status;
        }

        parameters["offset"] = offset;
        parameters["limit"]  = pageSize;

        var sql = $@"
            SELECT ae.*,
                   u.username AS created_by_username,
                   COUNT(*) OVER() AS total_count,
                   {(viewerId.HasValue ? "ap.user_id IS NOT NULL AS has_joined, aps.story_id IS NOT NULL AS has_submitted" : "NULL AS has_joined, NULL AS has_submitted")}
            FROM arena_events ae
            JOIN users u ON u.id = ae.created_by
            {(viewerId.HasValue ? @"
            LEFT JOIN arena_participants ap ON ap.event_id = ae.id AND ap.user_id = @vid
            LEFT JOIN arena_stories aps ON aps.event_id = ae.id AND aps.author_id = @vid AND aps.status = 'submitted'" : "")}
            {where}
            ORDER BY ae.created_at DESC
            LIMIT @limit OFFSET @offset";

        if (viewerId.HasValue) parameters["vid"] = viewerId.Value;

        long totalCount = 0;
        var items = await DbHelper.ExecuteReaderAsync<ArenaEventResponse>(conn, sql,
            r =>
            {
                totalCount = DbHelper.GetLong(r, "total_count");
                return MapArenaEvent(r, viewerId.HasValue);
            }, parameters);

        return new PagedResult<ArenaEventResponse>
        {
            Items      = items,
            TotalCount = (int)totalCount,
            Page       = page,
            PageSize   = pageSize,
        };
    }

    public async Task<ArenaEventResponse?> GetEventByIdAsync(Guid eventId, Guid? viewerId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        var sql = $@"
            SELECT ae.*,
                   u.username AS created_by_username,
                   {(viewerId.HasValue ? "ap.user_id IS NOT NULL AS has_joined, aps.story_id IS NOT NULL AS has_submitted" : "NULL AS has_joined, NULL AS has_submitted")}
            FROM arena_events ae
            JOIN users u ON u.id = ae.created_by
            {(viewerId.HasValue ? @"
            LEFT JOIN arena_participants ap ON ap.event_id = ae.id AND ap.user_id = @vid
            LEFT JOIN arena_stories aps ON aps.event_id = ae.id AND aps.author_id = @vid AND aps.status = 'submitted'" : "")}
            WHERE ae.id = @id AND ae.deleted_at IS NULL";

        var parameters = new Dictionary<string, object?> { ["id"] = eventId };
        if (viewerId.HasValue) parameters["vid"] = viewerId.Value;

        return await DbHelper.ExecuteReaderFirstAsync(conn, sql,
            r => MapArenaEvent(r, viewerId.HasValue), parameters);
    }

    public async Task<ArenaEventStatsResponse?> GetEventStatsAsync(Guid eventId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        return await DbHelper.ExecuteReaderFirstAsync(conn,
            @"SELECT
                ae.id AS event_id,
                COUNT(DISTINCT ap.user_id)                                        AS total_participants,
                COUNT(DISTINCT ars.id) FILTER (WHERE ars.status = 'submitted')    AS total_submitted,
                COUNT(DISTINCT ap.user_id) FILTER (WHERE ap.status='disqualified')AS total_disqualified,
                COUNT(DISTINCT ara.id) FILTER (WHERE ara.status = 'completed')    AS total_reviews_completed,
                COUNT(DISTINCT ara.id)                                            AS total_reviews_assigned,
                COUNT(DISTINCT ara.id) FILTER (WHERE ara.status = 'defaulted')   AS total_reviews_defaulted,
                ae.entry_fee_coins * COUNT(DISTINCT ap.user_id)                  AS total_coins_collected,
                COALESCE(ae.forfeit_pool, 0)                                      AS forfeit_pool_balance
              FROM arena_events ae
              LEFT JOIN arena_participants ap ON ap.event_id = ae.id
              LEFT JOIN arena_stories ars ON ars.event_id = ae.id
              LEFT JOIN arena_review_assignments ara ON ara.event_id = ae.id
              WHERE ae.id = @id AND ae.deleted_at IS NULL
              GROUP BY ae.id",
            r => new ArenaEventStatsResponse
            {
                EventId                = DbHelper.GetGuid(r, "event_id"),
                TotalParticipants      = DbHelper.GetInt(r, "total_participants"),
                TotalSubmitted         = DbHelper.GetInt(r, "total_submitted"),
                TotalDisqualified      = DbHelper.GetInt(r, "total_disqualified"),
                TotalReviewsCompleted  = DbHelper.GetInt(r, "total_reviews_completed"),
                TotalReviewsAssigned   = DbHelper.GetInt(r, "total_reviews_assigned"),
                TotalReviewsDefaulted  = DbHelper.GetInt(r, "total_reviews_defaulted"),
                TotalCoinsCollected    = DbHelper.GetLong(r, "total_coins_collected"),
                ForfeitPoolBalance     = DbHelper.GetLong(r, "forfeit_pool_balance"),
            },
            new Dictionary<string, object?> { ["id"] = eventId });
    }

    // ───────────────────────────────────────────────────────────────────────
    //  PARTICIPANT FLOW
    // ───────────────────────────────────────────────────────────────────────

    public async Task<(bool Success, string Message)> JoinEventAsync(Guid userId, Guid eventId)
    {
        await using var conn = await _db.CreateConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            // Lock event row
            var ev = await DbHelper.ExecuteReaderFirstAsync(conn,
                @"SELECT id, status, entry_fee_coins, min_participants_threshold,
                         writing_phase_ends_at, title
                  FROM arena_events WHERE id = @id AND deleted_at IS NULL FOR UPDATE",
                r => new
                {
                    Id         = DbHelper.GetGuid(r, "id"),
                    Status     = DbHelper.GetString(r, "status"),
                    Fee        = DbHelper.GetInt(r, "entry_fee_coins"),
                    Title      = DbHelper.GetString(r, "title"),
                    WritingEnd = DbHelper.GetDateTime(r, "writing_phase_ends_at"),
                },
                new Dictionary<string, object?> { ["id"] = eventId }, tx);

            if (ev == null) return (false, "Event nahi mila");
            if (ev.Status != "writing" && ev.Status != "upcoming")
                return (false, "Is event mein abhi join nahi kar sakte");
            if (ev.WritingEnd <= DateTime.UtcNow)
                return (false, "Writing phase khatam ho gayi. Join nahi kar sakte.");

            // Check already joined
            var alreadyJoined = await DbHelper.ExecuteScalarAsync<bool>(conn,
                "SELECT EXISTS(SELECT 1 FROM arena_participants WHERE event_id = @eid AND user_id = @uid)",
                new Dictionary<string, object?> { ["eid"] = eventId, ["uid"] = userId }, tx);
            if (alreadyJoined) return (false, "Already join kar liya hai");

            // Lock wallet and check balance
            var wallet = await DbHelper.ExecuteReaderFirstAsync(conn,
                "SELECT coin_balance, is_frozen FROM wallets WHERE user_id = @uid FOR UPDATE",
                r => new
                {
                    Balance  = DbHelper.GetLong(r, "coin_balance"),
                    IsFrozen = DbHelper.GetBool(r, "is_frozen"),
                },
                new Dictionary<string, object?> { ["uid"] = userId }, tx);

            if (wallet == null) return (false, "Wallet nahi mila. Support se contact karein.");
            if (wallet.IsFrozen) return (false, "Wallet frozen hai. Support se contact karein.");
            if (wallet.Balance < ev.Fee)
                return (false, $"Insufficient coins. {ev.Fee} coins chahiye, tumhare paas {wallet.Balance} hain.");

            // Deduct entry fee
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"UPDATE wallets
                  SET coin_balance = coin_balance - @fee,
                      total_spent  = total_spent + @fee,
                      last_transaction_at = NOW(),
                      updated_at   = NOW()
                  WHERE user_id = @uid",
                new Dictionary<string, object?> { ["fee"] = ev.Fee, ["uid"] = userId }, tx);

            // Log transaction
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO coin_transactions (id, sender_id, receiver_id, amount, type, description, status, completed_at)
                  VALUES (gen_random_uuid(), @uid, NULL, @fee, 'arena_entry_fee',
                          'Darr Arena entry fee: ' || @title, 'completed', NOW())",
                new Dictionary<string, object?>
                {
                    ["uid"]   = userId,
                    ["fee"]   = ev.Fee,
                    ["title"] = ev.Title,
                }, tx);

            // Add to prize pool (live)
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE arena_events SET prize_pot_live = COALESCE(prize_pot_live, 0) + @fee, updated_at = NOW() WHERE id = @id",
                new Dictionary<string, object?> { ["fee"] = ev.Fee, ["id"] = eventId }, tx);

            // Register participant
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO arena_participants (id, event_id, user_id, status, joined_at)
                  VALUES (gen_random_uuid(), @eid, @uid, 'active'::arena_participant_status, NOW())",
                new Dictionary<string, object?> { ["eid"] = eventId, ["uid"] = userId }, tx);

            // Create empty draft story
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO arena_stories (id, event_id, author_id, title, content, word_count, status, created_at, updated_at)
                  VALUES (gen_random_uuid(), @eid, @uid, '', '', 0, 'draft'::arena_story_status, NOW(), NOW())",
                new Dictionary<string, object?> { ["eid"] = eventId, ["uid"] = userId }, tx);

            await tx.CommitAsync();
            return (true, $"Darr Arena join kar liya! Ab apni horror kahani likho. Entry fee: {ev.Fee} coins.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "JoinEvent failed for user {UserId} event {EventId}", userId, eventId);
            return (false, "Join karte waqt error aaya. Dobara koshish karein.");
        }
    }

    public async Task<(bool Success, string Message)> SaveDraftAsync(
        Guid userId, Guid eventId, SaveDraftRequest req)
    {
        await using var conn = await _db.CreateConnectionAsync();

        // Verify event is in writing phase
        var ev = await DbHelper.ExecuteReaderFirstAsync(conn,
            @"SELECT status, writing_phase_ends_at, min_word_limit, max_word_limit
              FROM arena_events WHERE id = @id AND deleted_at IS NULL",
            r => new
            {
                Status     = DbHelper.GetString(r, "status"),
                WritingEnd = DbHelper.GetDateTime(r, "writing_phase_ends_at"),
                MinW       = DbHelper.GetInt(r, "min_word_limit"),
                MaxW       = DbHelper.GetInt(r, "max_word_limit"),
            },
            new Dictionary<string, object?> { ["id"] = eventId });

        if (ev == null) return (false, "Event nahi mila");
        if (ev.WritingEnd <= DateTime.UtcNow && ev.Status != "writing")
            return (false, "Writing phase khatam ho gayi");

        // Verify participant
        var story = await DbHelper.ExecuteReaderFirstAsync(conn,
            @"SELECT id, status FROM arena_stories
              WHERE event_id = @eid AND author_id = @uid",
            r => new
            {
                Id     = DbHelper.GetGuid(r, "id"),
                Status = DbHelper.GetString(r, "status"),
            },
            new Dictionary<string, object?> { ["eid"] = eventId, ["uid"] = userId });

        if (story == null) return (false, "Pehle event join karo");
        if (story.Status == "submitted") return (false, "Story pehle se submit ho chuki hai");
        if (story.Status == "locked") return (false, "Story lock ho gayi hai");

        // Server-side word count validation (client-supplied word_count just stored)
        var serverWordCount = CountWords(req.Content);

        await DbHelper.ExecuteNonQueryAsync(conn,
            @"UPDATE arena_stories
              SET title          = @title,
                  content        = @content,
                  word_count     = @wc,
                  draft_saved_at = NOW(),
                  updated_at     = NOW()
              WHERE id = @sid",
            new Dictionary<string, object?>
            {
                ["title"]   = req.Title,
                ["content"] = req.Content,
                ["wc"]      = serverWordCount,
                ["sid"]     = story.Id,
            });

        return (true, "Draft save ho gaya");
    }

    public async Task<(bool Success, string Message)> SubmitStoryAsync(Guid userId, Guid eventId)
    {
        await using var conn = await _db.CreateConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            var ev = await DbHelper.ExecuteReaderFirstAsync(conn,
                @"SELECT status, writing_phase_ends_at, min_word_limit, max_word_limit
                  FROM arena_events WHERE id = @id AND deleted_at IS NULL FOR UPDATE",
                r => new
                {
                    Status     = DbHelper.GetString(r, "status"),
                    WritingEnd = DbHelper.GetDateTime(r, "writing_phase_ends_at"),
                    MinW       = DbHelper.GetInt(r, "min_word_limit"),
                    MaxW       = DbHelper.GetInt(r, "max_word_limit"),
                },
                new Dictionary<string, object?> { ["id"] = eventId }, tx);

            if (ev == null) return (false, "Event nahi mila");
            if (ev.WritingEnd > DateTime.UtcNow)
                return (false, "Writing phase abhi khatam nahi hui. Phase end hone ke baad submit karo.");

            var story = await DbHelper.ExecuteReaderFirstAsync(conn,
                @"SELECT id, status, word_count, content, title
                  FROM arena_stories WHERE event_id = @eid AND author_id = @uid FOR UPDATE",
                r => new
                {
                    Id        = DbHelper.GetGuid(r, "id"),
                    Status    = DbHelper.GetString(r, "status"),
                    WordCount = DbHelper.GetInt(r, "word_count"),
                    Content   = DbHelper.GetString(r, "content"),
                    Title     = DbHelper.GetString(r, "title"),
                },
                new Dictionary<string, object?> { ["eid"] = eventId, ["uid"] = userId }, tx);

            if (story == null) return (false, "Story nahi mili");
            if (story.Status == "submitted") return (false, "Pehle se submit ho chuki hai");
            if (story.WordCount < ev.MinW)
                return (false, $"Story too short. Minimum {ev.MinW} words chahiye, tumne {story.WordCount} likhe hain.");
            if (story.WordCount > ev.MaxW)
                return (false, $"Story too long. Maximum {ev.MaxW} words allowed, tumne {story.WordCount} likhe hain.");
            if (string.IsNullOrWhiteSpace(story.Title))
                return (false, "Story ka title dena zaroori hai");

            // Mark submitted
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"UPDATE arena_stories
                  SET status       = 'submitted'::arena_story_status,
                      submitted_at = NOW(),
                      updated_at   = NOW()
                  WHERE id = @sid",
                new Dictionary<string, object?> { ["sid"] = story.Id }, tx);

            await tx.CommitAsync();
            return (true, "Story submit ho gayi! Ab 3 MCQ comprehension questions set karo (72 ghante ke andar).");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "SubmitStory failed for user {UserId} event {EventId}", userId, eventId);
            return (false, "Submit karte waqt error aaya");
        }
    }

    public async Task<(bool Success, string Message)> SubmitQuestionsAsync(
        Guid userId, Guid eventId, SubmitQuestionsRequest req)
    {
        if (req.Questions.Count != 3)
            return (false, "Exactly 3 questions submit karne hain");

        // Validate each question has minimum 10 words
        foreach (var q in req.Questions)
        {
            if (CountWords(q.QuestionText) < 2)
                return (false, "Question text too short — at least a proper question likhna hai");
        }

        await using var conn = await _db.CreateConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            var story = await DbHelper.ExecuteReaderFirstAsync(conn,
                @"SELECT id, status, questions_submitted
                  FROM arena_stories WHERE event_id = @eid AND author_id = @uid FOR UPDATE",
                r => new
                {
                    Id                  = DbHelper.GetGuid(r, "id"),
                    Status              = DbHelper.GetString(r, "status"),
                    QuestionsSubmitted  = DbHelper.GetBool(r, "questions_submitted"),
                },
                new Dictionary<string, object?> { ["eid"] = eventId, ["uid"] = userId }, tx);

            if (story == null) return (false, "Story nahi mili");
            if (story.Status != "submitted") return (false, "Pehle story submit karo");
            if (story.QuestionsSubmitted) return (false, "Questions pehle se submit ho chuke hain — change allowed nahi");

            // Insert 3 questions
            for (int i = 0; i < req.Questions.Count; i++)
            {
                var q = req.Questions[i];
                await DbHelper.ExecuteNonQueryAsync(conn,
                    @"INSERT INTO arena_story_questions
                      (id, story_id, question_order, question_text,
                       option_a, option_b, option_c, option_d, correct_option, created_at)
                      VALUES (gen_random_uuid(), @sid, @ord, @qt, @a, @b, @c, @d, @correct, NOW())",
                    new Dictionary<string, object?>
                    {
                        ["sid"]     = story.Id,
                        ["ord"]     = i + 1,
                        ["qt"]      = q.QuestionText,
                        ["a"]       = q.OptionA,
                        ["b"]       = q.OptionB,
                        ["c"]       = q.OptionC,
                        ["d"]       = q.OptionD,
                        ["correct"] = q.CorrectOption,
                    }, tx);
            }

            // Lock questions permanently
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"UPDATE arena_stories
                  SET questions_submitted = TRUE,
                      status = 'locked'::arena_story_status,
                      updated_at = NOW()
                  WHERE id = @sid",
                new Dictionary<string, object?> { ["sid"] = story.Id }, tx);

            await tx.CommitAsync();
            return (true, "3 MCQ questions lock ho gaye! Ab reviewers inhe padhenge.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "SubmitQuestions failed for user {UserId} event {EventId}", userId, eventId);
            return (false, "Questions submit karte waqt error aaya");
        }
    }

    public async Task<ArenaStoryResponse?> GetMyStoryAsync(Guid userId, Guid eventId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        var story = await DbHelper.ExecuteReaderFirstAsync(conn,
            @"SELECT id, event_id, title, content, word_count, status,
                     questions_submitted, draft_saved_at, submitted_at
              FROM arena_stories
              WHERE event_id = @eid AND author_id = @uid",
            r => new ArenaStoryResponse
            {
                Id                 = DbHelper.GetGuid(r, "id"),
                EventId            = DbHelper.GetGuid(r, "event_id"),
                Title              = DbHelper.GetString(r, "title"),
                Content            = DbHelper.GetString(r, "content"),
                WordCount          = DbHelper.GetInt(r, "word_count"),
                Status             = DbHelper.GetString(r, "status"),
                QuestionsSubmitted = DbHelper.GetBool(r, "questions_submitted"),
                DraftSavedAt       = DbHelper.GetDateTimeOrNull(r, "draft_saved_at"),
                SubmittedAt        = DbHelper.GetDateTimeOrNull(r, "submitted_at"),
            },
            new Dictionary<string, object?> { ["eid"] = eventId, ["uid"] = userId });

        if (story == null) return null;

        // Include questions if submitted (author can see their own questions)
        if (story.QuestionsSubmitted)
        {
            story.Questions = await GetStoryQuestionsAsync(conn, story.Id, includeCorrect: false);
        }

        return story;
    }

    public async Task<SubmitStatusResponse?> GetSubmitStatusAsync(Guid userId, Guid eventId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        var ev = await DbHelper.ExecuteReaderFirstAsync(conn,
            @"SELECT status, writing_phase_ends_at, min_word_limit, max_word_limit
              FROM arena_events WHERE id = @id AND deleted_at IS NULL",
            r => new
            {
                Status     = DbHelper.GetString(r, "status"),
                WritingEnd = DbHelper.GetDateTime(r, "writing_phase_ends_at"),
                MinW       = DbHelper.GetInt(r, "min_word_limit"),
                MaxW       = DbHelper.GetInt(r, "max_word_limit"),
            },
            new Dictionary<string, object?> { ["id"] = eventId });

        if (ev == null) return null;

        var story = await DbHelper.ExecuteReaderFirstAsync(conn,
            "SELECT status, word_count, questions_submitted FROM arena_stories WHERE event_id = @eid AND author_id = @uid",
            r => new
            {
                Status             = DbHelper.GetString(r, "status"),
                WordCount          = DbHelper.GetInt(r, "word_count"),
                QuestionsSubmitted = DbHelper.GetBool(r, "questions_submitted"),
            },
            new Dictionary<string, object?> { ["eid"] = eventId, ["uid"] = userId });

        bool writingEnded = ev.WritingEnd <= DateTime.UtcNow;
        TimeSpan? remaining = writingEnded ? null : ev.WritingEnd - DateTime.UtcNow;

        return new SubmitStatusResponse
        {
            WritingPhaseEnded  = writingEnded,
            CanSubmit          = writingEnded && story?.Status == "draft",
            HasSubmitted       = story?.Status is "submitted" or "locked",
            QuestionsSubmitted = story?.QuestionsSubmitted ?? false,
            WritingPhaseEndsAt = ev.WritingEnd,
            TimeRemaining      = remaining,
            WordCount          = story?.WordCount ?? 0,
            MeetsMinWordLimit  = (story?.WordCount ?? 0) >= ev.MinW,
        };
    }

    // ───────────────────────────────────────────────────────────────────────
    //  REVIEW PHASE
    // ───────────────────────────────────────────────────────────────────────

    public async Task<List<ArenaAssignmentResponse>> GetMyAssignmentsAsync(Guid userId, Guid eventId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        return await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT ara.id AS assignment_id, ara.event_id, ae.title AS event_title,
                     ara.story_id, ara.status, ara.read_time_verified, ara.scroll_verified,
                     ara.questions_passed, ara.wrong_attempts, ae.review_phase_ends_at,
                     ara.is_extra_review, ara.extra_coins_offered
              FROM arena_review_assignments ara
              JOIN arena_events ae ON ae.id = ara.event_id
              WHERE ara.event_id = @eid AND ara.assigned_to = @uid
              ORDER BY ara.is_extra_review ASC, ara.assigned_at ASC",
            r =>
            {
                var endsAt = DbHelper.GetDateTimeOrNull(r, "review_phase_ends_at");
                return new ArenaAssignmentResponse
                {
                    AssignmentId      = DbHelper.GetGuid(r, "assignment_id"),
                    EventId           = DbHelper.GetGuid(r, "event_id"),
                    EventTitle        = DbHelper.GetString(r, "event_title"),
                    StoryId           = DbHelper.GetGuid(r, "story_id"),
                    Status            = DbHelper.GetString(r, "status"),
                    ReadTimeVerified  = DbHelper.GetBool(r, "read_time_verified"),
                    ScrollVerified    = DbHelper.GetBool(r, "scroll_verified"),
                    QuestionsPassed   = DbHelper.GetBool(r, "questions_passed"),
                    WrongAttempts     = DbHelper.GetInt(r, "wrong_attempts"),
                    ReviewPhaseEndsAt = endsAt,
                    TimeRemaining     = endsAt.HasValue && endsAt > DateTime.UtcNow
                                        ? endsAt - DateTime.UtcNow : null,
                    IsExtraReview     = DbHelper.GetBool(r, "is_extra_review"),
                    ExtraCoinsOffered = DbHelper.GetInt(r, "extra_coins_offered"),
                };
            },
            new Dictionary<string, object?> { ["eid"] = eventId, ["uid"] = userId });
    }

    public async Task<ArenaStoryReviewResponse?> GetStoryForReviewAsync(Guid userId, Guid assignmentId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        var assignment = await DbHelper.ExecuteReaderFirstAsync(conn,
            @"SELECT ara.id, ara.story_id, ara.assigned_to, ara.status,
                     ara.read_time_verified, ara.scroll_verified, ara.questions_passed,
                     ara.wrong_attempts,
                     ars.title AS story_title, ars.content, ars.word_count
              FROM arena_review_assignments ara
              JOIN arena_stories ars ON ars.id = ara.story_id
              WHERE ara.id = @aid AND ara.assigned_to = @uid",
            r => new
            {
                Id               = DbHelper.GetGuid(r, "id"),
                StoryId          = DbHelper.GetGuid(r, "story_id"),
                AssignedTo       = DbHelper.GetGuid(r, "assigned_to"),
                Status           = DbHelper.GetString(r, "status"),
                ReadVerified     = DbHelper.GetBool(r, "read_time_verified"),
                ScrollVerified   = DbHelper.GetBool(r, "scroll_verified"),
                QuestionsPassed  = DbHelper.GetBool(r, "questions_passed"),
                WrongAttempts    = DbHelper.GetInt(r, "wrong_attempts"),
                StoryTitle       = DbHelper.GetString(r, "story_title"),
                Content          = DbHelper.GetString(r, "content"),
                WordCount        = DbHelper.GetInt(r, "word_count"),
            },
            new Dictionary<string, object?> { ["aid"] = assignmentId, ["uid"] = userId });

        if (assignment == null) return null;
        if (assignment.Status == "defaulted" || assignment.Status == "disqualified")
            return null;

        int minReadSeconds = (int)Math.Ceiling(assignment.WordCount / 200.0 * 60.0);

        var response = new ArenaStoryReviewResponse
        {
            AssignmentId      = assignment.Id,
            StoryId           = assignment.StoryId,
            StoryTitle        = assignment.StoryTitle,
            Content           = assignment.Content,
            WordCount         = assignment.WordCount,
            MinReadTimeSeconds = minReadSeconds,
            ReadTimeVerified  = assignment.ReadVerified,
            ScrollVerified    = assignment.ScrollVerified,
            QuestionsPassed   = assignment.QuestionsPassed,
            WrongAttempts     = assignment.WrongAttempts,
        };

        // Only expose questions AFTER read time + scroll verified
        if (assignment.ReadVerified && assignment.ScrollVerified)
        {
            response.Questions = await GetStoryQuestionsAsync(conn, assignment.StoryId, includeCorrect: false);
        }

        return response;
    }

    public async Task<(bool Success, string Message)> VerifyReadTimeAsync(Guid userId, Guid assignmentId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        var assignment = await DbHelper.ExecuteReaderFirstAsync(conn,
            @"SELECT ara.id, ara.assigned_to, ara.status, ara.read_time_verified,
                     ars.word_count, ara.read_started_at
              FROM arena_review_assignments ara
              JOIN arena_stories ars ON ars.id = ara.story_id
              WHERE ara.id = @aid",
            r => new
            {
                Id              = DbHelper.GetGuid(r, "id"),
                AssignedTo      = DbHelper.GetGuid(r, "assigned_to"),
                Status          = DbHelper.GetString(r, "status"),
                ReadVerified    = DbHelper.GetBool(r, "read_time_verified"),
                WordCount       = DbHelper.GetInt(r, "word_count"),
                ReadStartedAt   = DbHelper.GetDateTimeOrNull(r, "read_started_at"),
            },
            new Dictionary<string, object?> { ["aid"] = assignmentId });

        if (assignment == null || assignment.AssignedTo != userId)
            return (false, "Assignment nahi mila");
        if (assignment.Status is "completed" or "defaulted" or "disqualified")
            return (false, "Assignment already closed hai");
        if (assignment.ReadVerified) return (true, "Already verified");

        // Calculate minimum read time (word_count ÷ 200 wpm)
        double minReadMinutes = assignment.WordCount / 200.0;
        double minReadSeconds = minReadMinutes * 60.0;

        // Verify actual elapsed time
        if (assignment.ReadStartedAt == null)
        {
            // First call — record start time
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE arena_review_assignments SET read_started_at = NOW() WHERE id = @aid",
                new Dictionary<string, object?> { ["aid"] = assignmentId });
            return (false, $"Timer shuru ho gaya. Minimum {(int)Math.Ceiling(minReadSeconds)} seconds tak padhna hai.");
        }

        double elapsed = (DateTime.UtcNow - assignment.ReadStartedAt.Value).TotalSeconds;
        if (elapsed < minReadSeconds)
        {
            int remaining = (int)Math.Ceiling(minReadSeconds - elapsed);
            return (false, $"Aur {remaining} seconds padhna hai. Jaldi mat karo — anti-cheat active hai.");
        }

        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE arena_review_assignments SET read_time_verified = TRUE, updated_at = NOW() WHERE id = @aid",
            new Dictionary<string, object?> { ["aid"] = assignmentId });

        return (true, "Read time verify ho gaya! Ab scroll complete karo.");
    }

    public async Task<(bool Success, string Message)> VerifyScrollAsync(Guid userId, Guid assignmentId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        var assignment = await DbHelper.ExecuteReaderFirstAsync(conn,
            @"SELECT id, assigned_to, status, read_time_verified, scroll_verified
              FROM arena_review_assignments WHERE id = @aid",
            r => new
            {
                Id             = DbHelper.GetGuid(r, "id"),
                AssignedTo     = DbHelper.GetGuid(r, "assigned_to"),
                Status         = DbHelper.GetString(r, "status"),
                ReadVerified   = DbHelper.GetBool(r, "read_time_verified"),
                ScrollVerified = DbHelper.GetBool(r, "scroll_verified"),
            },
            new Dictionary<string, object?> { ["aid"] = assignmentId });

        if (assignment == null || assignment.AssignedTo != userId)
            return (false, "Assignment nahi mila");
        if (!assignment.ReadVerified)
            return (false, "Pehle minimum read time complete karo");
        if (assignment.ScrollVerified) return (true, "Already verified");

        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE arena_review_assignments SET scroll_verified = TRUE, updated_at = NOW() WHERE id = @aid",
            new Dictionary<string, object?> { ["aid"] = assignmentId });

        return (true, "Scroll verified! Ab 3 MCQ questions answer karo.");
    }

    public async Task<(bool Success, string Message, AnswerQuestionsResult? Data)> AnswerQuestionsAsync(
        Guid userId, Guid assignmentId, AnswerQuestionsRequest req)
    {
        await using var conn = await _db.CreateConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            var assignment = await DbHelper.ExecuteReaderFirstAsync(conn,
                @"SELECT ara.id, ara.assigned_to, ara.event_id, ara.story_id, ara.status,
                         ara.read_time_verified, ara.scroll_verified, ara.questions_passed,
                         ara.wrong_attempts
                  FROM arena_review_assignments ara
                  WHERE ara.id = @aid FOR UPDATE",
                r => new
                {
                    Id              = DbHelper.GetGuid(r, "id"),
                    AssignedTo      = DbHelper.GetGuid(r, "assigned_to"),
                    EventId         = DbHelper.GetGuid(r, "event_id"),
                    StoryId         = DbHelper.GetGuid(r, "story_id"),
                    Status          = DbHelper.GetString(r, "status"),
                    ReadVerified    = DbHelper.GetBool(r, "read_time_verified"),
                    ScrollVerified  = DbHelper.GetBool(r, "scroll_verified"),
                    QuestionsPassed = DbHelper.GetBool(r, "questions_passed"),
                    WrongAttempts   = DbHelper.GetInt(r, "wrong_attempts"),
                },
                new Dictionary<string, object?> { ["aid"] = assignmentId }, tx);

            if (assignment == null || assignment.AssignedTo != userId)
                return (false, "Assignment nahi mila", null);
            if (!assignment.ReadVerified || !assignment.ScrollVerified)
                return (false, "Pehle read + scroll verify karo", null);
            if (assignment.QuestionsPassed)
                return (false, "Questions pehle se pass ho chuke hain", null);
            if (assignment.Status is "completed" or "defaulted" or "disqualified")
                return (false, "Assignment closed hai", null);

            // Fetch correct answers
            var questions = await DbHelper.ExecuteReaderAsync(conn,
                @"SELECT question_order, correct_option
                  FROM arena_story_questions
                  WHERE story_id = @sid ORDER BY question_order",
                r => new
                {
                    Order   = DbHelper.GetInt(r, "question_order"),
                    Correct = DbHelper.GetString(r, "correct_option"),
                },
                new Dictionary<string, object?> { ["sid"] = assignment.StoryId }, tx);

            if (questions.Count != 3)
                return (false, "Questions nahi mile. Support se contact karein.", null);

            string[] answers = { req.AnswerQ1, req.AnswerQ2, req.AnswerQ3 };
            int correctCount = 0;
            for (int i = 0; i < 3; i++)
            {
                if (questions[i].Correct.Equals(answers[i], StringComparison.OrdinalIgnoreCase))
                    correctCount++;
            }

            bool allCorrect = correctCount == 3;
            int newWrongAttempts = assignment.WrongAttempts + (allCorrect ? 0 : 1);

            string? message;
            bool disqualified = false;
            bool mustReRead = false;

            if (allCorrect)
            {
                // Pass — mark questions_passed
                await DbHelper.ExecuteNonQueryAsync(conn,
                    @"UPDATE arena_review_assignments
                      SET questions_passed = TRUE, updated_at = NOW()
                      WHERE id = @aid",
                    new Dictionary<string, object?> { ["aid"] = assignmentId }, tx);
                message = "Sahi jawab! Ab rating do.";
            }
            else
            {
                // Increment wrong attempts
                await DbHelper.ExecuteNonQueryAsync(conn,
                    @"UPDATE arena_review_assignments
                      SET wrong_attempts = @wa, updated_at = NOW()
                      WHERE id = @aid",
                    new Dictionary<string, object?> { ["aid"] = assignmentId, ["wa"] = newWrongAttempts }, tx);

                switch (newWrongAttempts)
                {
                    case WarnLevel1:
                        message = "Galat jawab! Dhyan se padho. (Chetavni 1/3)";
                        break;
                    case WarnLevel2:
                        message = $"Phir galat! Sirf ek aur mauka hai. {correctCount}/3 sahi the. (Chetavni 2/3)";
                        break;
                    case WarnLevel3:
                        message = "Aakhri chetavni! Agli baar galat hua toh story phir se padhni hogi. (Chetavni 3/3)";
                        break;
                    case ReReadLevel:
                        // Reset read+scroll verification — must re-read
                        mustReRead = true;
                        await DbHelper.ExecuteNonQueryAsync(conn,
                            @"UPDATE arena_review_assignments
                              SET read_time_verified = FALSE, scroll_verified = FALSE,
                                  read_started_at = NULL, updated_at = NOW()
                              WHERE id = @aid",
                            new Dictionary<string, object?> { ["aid"] = assignmentId }, tx);
                        message = "4 baar galat! Story ki samajh nahi aayi. Phir se poori story padho aur scroll karo.";
                        break;
                    default: // >= DisqualLevel
                        disqualified = true;
                        await DbHelper.ExecuteNonQueryAsync(conn,
                            @"UPDATE arena_review_assignments
                              SET status = 'disqualified'::arena_assignment_status, updated_at = NOW()
                              WHERE id = @aid",
                            new Dictionary<string, object?> { ["aid"] = assignmentId }, tx);
                        // Also mark participant as disqualified
                        await DbHelper.ExecuteNonQueryAsync(conn,
                            @"UPDATE arena_participants
                              SET status = 'disqualified'::arena_participant_status, updated_at = NOW()
                              WHERE event_id = @eid AND user_id = @uid",
                            new Dictionary<string, object?> { ["eid"] = assignment.EventId, ["uid"] = userId }, tx);
                        message = "5+ baar galat! Tum is event se disqualify ho gaye. Cheating nahi chalti Darr Arena mein.";
                        break;
                }
            }

            await tx.CommitAsync();

            return (true, message!, new AnswerQuestionsResult
            {
                AllCorrect      = allCorrect,
                CorrectCount    = correctCount,
                WrongAttempts   = newWrongAttempts,
                Disqualified    = disqualified,
                MustReReadStory = mustReRead,
                Message         = message,
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "AnswerQuestions failed for user {UserId} assignment {AssignmentId}", userId, assignmentId);
            return (false, "Error aaya. Dobara koshish karein.", null);
        }
    }

    public async Task<(bool Success, string Message)> SubmitRatingAsync(
        Guid userId, Guid assignmentId, SubmitRatingRequest req)
    {
        await using var conn = await _db.CreateConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            var assignment = await DbHelper.ExecuteReaderFirstAsync(conn,
                @"SELECT id, assigned_to, event_id, story_id, status,
                         read_time_verified, scroll_verified, questions_passed,
                         is_extra_review, extra_coins_offered
                  FROM arena_review_assignments WHERE id = @aid FOR UPDATE",
                r => new
                {
                    Id               = DbHelper.GetGuid(r, "id"),
                    AssignedTo       = DbHelper.GetGuid(r, "assigned_to"),
                    EventId          = DbHelper.GetGuid(r, "event_id"),
                    StoryId          = DbHelper.GetGuid(r, "story_id"),
                    Status           = DbHelper.GetString(r, "status"),
                    ReadVerified     = DbHelper.GetBool(r, "read_time_verified"),
                    ScrollVerified   = DbHelper.GetBool(r, "scroll_verified"),
                    QuestionsPassed  = DbHelper.GetBool(r, "questions_passed"),
                    IsExtraReview    = DbHelper.GetBool(r, "is_extra_review"),
                    ExtraCoins       = DbHelper.GetInt(r, "extra_coins_offered"),
                },
                new Dictionary<string, object?> { ["aid"] = assignmentId }, tx);

            if (assignment == null || assignment.AssignedTo != userId)
                return (false, "Assignment nahi mila");
            if (!assignment.ReadVerified || !assignment.ScrollVerified || !assignment.QuestionsPassed)
                return (false, "Pehle read + scroll + MCQ complete karo");
            if (assignment.Status == "completed")
                return (false, "Rating pehle se submit ho chuki hai");
            if (assignment.Status is "defaulted" or "disqualified")
                return (false, "Assignment closed hai");

            // Save rating
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO arena_ratings
                  (id, assignment_id, event_id, story_id, rated_by, rating, comment, rated_at)
                  VALUES (gen_random_uuid(), @aid, @eid, @sid, @uid, @rating, @comment, NOW())",
                new Dictionary<string, object?>
                {
                    ["aid"]     = assignmentId,
                    ["eid"]     = assignment.EventId,
                    ["sid"]     = assignment.StoryId,
                    ["uid"]     = userId,
                    ["rating"]  = req.Rating,
                    ["comment"] = req.Comment,
                }, tx);

            // Mark assignment completed
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"UPDATE arena_review_assignments
                  SET status = 'completed'::arena_assignment_status,
                      completed_at = NOW(), updated_at = NOW()
                  WHERE id = @aid",
                new Dictionary<string, object?> { ["aid"] = assignmentId }, tx);

            // If extra review — pay extra coins
            if (assignment.IsExtraReview && assignment.ExtraCoins > 0)
            {
                await DbHelper.ExecuteNonQueryAsync(conn,
                    @"UPDATE wallets
                      SET coin_balance = coin_balance + @coins,
                          total_earned = total_earned + @coins,
                          last_transaction_at = NOW(), updated_at = NOW()
                      WHERE user_id = @uid",
                    new Dictionary<string, object?> { ["coins"] = assignment.ExtraCoins, ["uid"] = userId }, tx);

                await DbHelper.ExecuteNonQueryAsync(conn,
                    @"INSERT INTO coin_transactions (id, sender_id, receiver_id, amount, type, description, status, completed_at)
                      VALUES (gen_random_uuid(), NULL, @uid, @coins, 'arena_extra_review_bonus',
                              'Extra review bonus from forfeit pool', 'completed', NOW())",
                    new Dictionary<string, object?> { ["uid"] = userId, ["coins"] = assignment.ExtraCoins }, tx);

                // Deduct from forfeit pool
                await DbHelper.ExecuteNonQueryAsync(conn,
                    "UPDATE arena_events SET forfeit_pool = forfeit_pool - @coins, updated_at = NOW() WHERE id = @eid",
                    new Dictionary<string, object?> { ["coins"] = assignment.ExtraCoins, ["eid"] = assignment.EventId }, tx);
            }

            await tx.CommitAsync();
            return (true, assignment.IsExtraReview
                ? $"Rating submit! Aur {assignment.ExtraCoins} bonus coins mile extra review ke liye!"
                : "Rating submit ho gayi!");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "SubmitRating failed for user {UserId} assignment {AssignmentId}", userId, assignmentId);
            return (false, "Rating submit karte waqt error aaya");
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    //  RESULTS
    // ───────────────────────────────────────────────────────────────────────

    public async Task<ArenaResultsResponse?> GetEventResultsAsync(Guid eventId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        var ev = await DbHelper.ExecuteReaderFirstAsync(conn,
            @"SELECT id, title, topic, original_prize_pool, status, completed_at,
                     (SELECT COUNT(*) FROM arena_participants WHERE event_id = ae.id) AS total_participants
              FROM arena_events ae WHERE id = @id AND deleted_at IS NULL",
            r => new
            {
                Id               = DbHelper.GetGuid(r, "id"),
                Title            = DbHelper.GetString(r, "title"),
                Topic            = DbHelper.GetString(r, "topic"),
                OriginalPrize    = DbHelper.GetLong(r, "original_prize_pool"),
                Status           = DbHelper.GetString(r, "status"),
                CompletedAt      = DbHelper.GetDateTimeOrNull(r, "completed_at"),
                TotalParticipants = DbHelper.GetInt(r, "total_participants"),
            },
            new Dictionary<string, object?> { ["id"] = eventId });

        if (ev == null || ev.Status != "completed") return null;

        var winners = await GetEventWinnersAsync(conn, eventId);
        var leaderboard = await GetLeaderboardAsync(conn, eventId);

        return new ArenaResultsResponse
        {
            EventId           = ev.Id,
            EventTitle        = ev.Title,
            Topic             = ev.Topic,
            TotalParticipants = ev.TotalParticipants,
            TotalPrizePool    = ev.OriginalPrize,
            Winners           = winners,
            Leaderboard       = leaderboard,
            AnnouncedAt       = ev.CompletedAt,
        };
    }

    public async Task<MyArenaResultResponse?> GetMyResultAsync(Guid userId, Guid eventId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        return await DbHelper.ExecuteReaderFirstAsync(conn,
            @"SELECT ae.id AS event_id, ae.title AS event_title,
                     aw.rank AS position, aw.coins_won,
                     (SELECT COUNT(*) FROM arena_participants WHERE event_id = ae.id) AS total_participants,
                     COALESCE(avg_r.avg_rating, 0) AS average_rating,
                     COALESCE(avg_r.total_reviews, 0) AS total_reviews,
                     avg_r.best_rating, avg_r.worst_rating,
                     ap.status AS participant_status,
                     ars.status AS story_status, ars.title AS story_title
              FROM arena_events ae
              JOIN arena_participants ap ON ap.event_id = ae.id AND ap.user_id = @uid
              LEFT JOIN arena_stories ars ON ars.event_id = ae.id AND ars.author_id = @uid
              LEFT JOIN arena_winners aw ON aw.event_id = ae.id AND aw.user_id = @uid
              LEFT JOIN LATERAL (
                  SELECT AVG(ar.rating)::DECIMAL(5,2) AS avg_rating,
                         COUNT(ar.id) AS total_reviews,
                         MAX(ar.rating) AS best_rating,
                         MIN(ar.rating) AS worst_rating
                  FROM arena_ratings ar
                  WHERE ar.story_id = ars.id
              ) avg_r ON TRUE
              WHERE ae.id = @eid AND ae.deleted_at IS NULL",
            r => new MyArenaResultResponse
            {
                EventId            = DbHelper.GetGuid(r, "event_id"),
                EventTitle         = DbHelper.GetString(r, "event_title"),
                Position           = DbHelper.GetInt(r, "position"),
                TotalParticipants  = DbHelper.GetInt(r, "total_participants"),
                AverageRating      = DbHelper.GetDecimal(r, "average_rating"),
                TotalReviews       = DbHelper.GetInt(r, "total_reviews"),
                BestRatingReceived = r.IsDBNull(r.GetOrdinal("best_rating")) ? null : DbHelper.GetDecimal(r, "best_rating"),
                WorstRatingReceived= r.IsDBNull(r.GetOrdinal("worst_rating")) ? null : DbHelper.GetDecimal(r, "worst_rating"),
                CoinsWon           = DbHelper.GetLong(r, "coins_won"),
                ParticipantStatus  = DbHelper.GetString(r, "participant_status"),
                StoryStatus        = DbHelper.GetStringOrNull(r, "story_status") ?? "",
                StoryTitle         = DbHelper.GetStringOrNull(r, "story_title"),
            },
            new Dictionary<string, object?> { ["eid"] = eventId, ["uid"] = userId });
    }

    public async Task<List<HallOfChampionsResponse>> GetHallOfChampionsAsync(int page, int pageSize)
    {
        await using var conn = await _db.CreateConnectionAsync();
        pageSize = Math.Min(pageSize, 20);
        int offset = (page - 1) * pageSize;

        var events = await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT ae.id, ae.title, ae.topic, ae.status,
                     ae.original_prize_pool AS total_coin_pool,
                     ae.hall_reading_cost_coins, ae.completed_at, ae.cancellation_reason,
                     (SELECT COUNT(*) FROM arena_participants WHERE event_id = ae.id) AS total_participants
              FROM arena_events ae
              WHERE ae.status IN ('completed','cancelled') AND ae.deleted_at IS NULL
              ORDER BY ae.completed_at DESC NULLS LAST
              LIMIT @limit OFFSET @offset",
            r => new HallOfChampionsResponse
            {
                EventId              = DbHelper.GetGuid(r, "id"),
                EventTitle           = DbHelper.GetString(r, "title"),
                Topic                = DbHelper.GetString(r, "topic"),
                Status               = DbHelper.GetString(r, "status"),
                TotalParticipants    = DbHelper.GetInt(r, "total_participants"),
                TotalCoinPool        = DbHelper.GetLong(r, "total_coin_pool"),
                HallReadingCostCoins = DbHelper.GetInt(r, "hall_reading_cost_coins"),
                CompletedAt          = DbHelper.GetDateTimeOrNull(r, "completed_at"),
                CancellationReason   = DbHelper.GetStringOrNull(r, "cancellation_reason"),
                Winners              = new List<ArenaWinnerResponse>(),
            },
            new Dictionary<string, object?> { ["limit"] = pageSize, ["offset"] = offset });

        // Populate winners for each event
        foreach (var ev in events)
        {
            ev.Winners = await GetEventWinnersAsync(conn, ev.EventId);
        }

        return events;
    }

    public async Task<MostFearedStoryResponse?> GetMostFearedStoryAsync(Guid eventId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        return await DbHelper.ExecuteReaderFirstAsync(conn,
            @"SELECT ae.id AS event_id, ae.title AS event_title,
                     ars.id AS story_id, ars.title AS story_title,
                     u.username AS author_username, u.avatar_url,
                     ae.hall_reading_cost_coins,
                     aw.average_rating, aw.total_reviews,
                     ae.completed_at AS achieved_at
              FROM arena_events ae
              JOIN arena_winners aw ON aw.event_id = ae.id AND aw.rank = 1
              JOIN arena_stories ars ON ars.id = aw.story_id
              JOIN users u ON u.id = aw.user_id
              WHERE ae.id = @id AND ae.status = 'completed' AND ae.deleted_at IS NULL",
            r => new MostFearedStoryResponse
            {
                EventId              = DbHelper.GetGuid(r, "event_id"),
                EventTitle           = DbHelper.GetString(r, "event_title"),
                StoryId              = DbHelper.GetGuid(r, "story_id"),
                StoryTitle           = DbHelper.GetString(r, "story_title"),
                AuthorUsername       = DbHelper.GetString(r, "author_username"),
                AuthorAvatarUrl      = DbHelper.GetStringOrNull(r, "avatar_url"),
                AverageRating        = DbHelper.GetDecimal(r, "average_rating"),
                TotalReviews         = DbHelper.GetInt(r, "total_reviews"),
                HallReadingCostCoins = DbHelper.GetInt(r, "hall_reading_cost_coins"),
                AchievedAt           = DbHelper.GetDateTime(r, "achieved_at"),
            },
            new Dictionary<string, object?> { ["id"] = eventId });
    }

    public async Task<List<ArenaBadgeResponse>> GetUserBadgesAsync(Guid userId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        return await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT ab.id AS badge_id, ab.event_id, ab.event_title,
                     ab.story_id, ab.badge_type, ab.rank, ab.coins_won, ab.awarded_at
              FROM arena_badges ab
              WHERE ab.user_id = @uid
              ORDER BY ab.awarded_at DESC",
            r => new ArenaBadgeResponse
            {
                BadgeId    = DbHelper.GetGuid(r, "badge_id"),
                EventId    = DbHelper.GetGuid(r, "event_id"),
                EventTitle = DbHelper.GetString(r, "event_title"),
                StoryId    = DbHelper.GetGuid(r, "story_id"),
                BadgeType  = DbHelper.GetString(r, "badge_type"),
                Rank       = DbHelper.GetInt(r, "rank"),
                CoinsWon   = DbHelper.GetLong(r, "coins_won"),
                AwardedAt  = DbHelper.GetDateTime(r, "awarded_at"),
            },
            new Dictionary<string, object?> { ["uid"] = userId });
    }

    // ───────────────────────────────────────────────────────────────────────
    //  BACKGROUND JOB HOOKS
    // ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by background job when writing phase ends.
    /// 1. Check min participants threshold — if not met, cancel and refund.
    /// 2. Freeze original_prize_pool.
    /// 3. Auto-submit non-submitted stories if word_count >= min.
    /// 4. Run fair shuffle story assignment (5 reviews per story, no self-review).
    /// 5. Transition event to 'review' status.
    /// </summary>
    public async Task TransitionToReviewPhaseAsync(Guid eventId)
    {
        await using var conn = await _db.CreateConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            var ev = await DbHelper.ExecuteReaderFirstAsync(conn,
                @"SELECT id, status, entry_fee_coins, min_participants_threshold,
                         review_phase_hours, prize_pot_live, title
                  FROM arena_events WHERE id = @id FOR UPDATE",
                r => new
                {
                    Id            = DbHelper.GetGuid(r, "id"),
                    Status        = DbHelper.GetString(r, "status"),
                    Fee           = DbHelper.GetInt(r, "entry_fee_coins"),
                    MinP          = DbHelper.GetInt(r, "min_participants_threshold"),
                    ReviewHours   = DbHelper.GetInt(r, "review_phase_hours"),
                    PrizePot      = DbHelper.GetLong(r, "prize_pot_live"),
                    Title         = DbHelper.GetString(r, "title"),
                },
                new Dictionary<string, object?> { ["id"] = eventId }, tx);

            if (ev == null || ev.Status != "writing")
            {
                _logger.LogWarning("TransitionToReview: Event {EventId} not in writing status", eventId);
                return;
            }

            // Count submitted stories
            var submittedCount = await DbHelper.ExecuteScalarAsync<int>(conn,
                @"SELECT COUNT(*) FROM arena_stories
                  WHERE event_id = @eid AND status = 'locked' AND questions_submitted = TRUE",
                new Dictionary<string, object?> { ["eid"] = eventId }, tx);

            // Auto-submit stories that meet word limit but weren't locked (questions not set)
            // Mark stories that DID submit but haven't set questions as void
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"UPDATE arena_stories
                  SET status = 'void'::arena_story_status, updated_at = NOW()
                  WHERE event_id = @eid
                    AND status = 'submitted'
                    AND questions_submitted = FALSE",
                new Dictionary<string, object?> { ["eid"] = eventId }, tx);

            // Also mark participants whose stories are void/draft as defaulted
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"UPDATE arena_participants ap
                  SET status = 'defaulted'::arena_participant_status, updated_at = NOW()
                  FROM arena_stories ars
                  WHERE ars.event_id = @eid
                    AND ars.author_id = ap.user_id
                    AND ap.event_id = @eid
                    AND ars.status IN ('draft','void','submitted')
                    AND ap.status = 'active'",
                new Dictionary<string, object?> { ["eid"] = eventId }, tx);

            // Re-count valid locked stories
            var validSubmissions = await DbHelper.ExecuteScalarAsync<int>(conn,
                @"SELECT COUNT(*) FROM arena_stories
                  WHERE event_id = @eid AND status = 'locked' AND questions_submitted = TRUE",
                new Dictionary<string, object?> { ["eid"] = eventId }, tx);

            // Check minimum threshold
            if (validSubmissions < ev.MinP)
            {
                _logger.LogWarning("Event {EventId} cancelled — only {Count} valid submissions, threshold {Min}",
                    eventId, validSubmissions, ev.MinP);
                await CancelDueToThresholdAsync(conn, tx, ev.Id, ev.Fee,
                    $"Minimum participants threshold ({ev.MinP}) not met. Only {validSubmissions} valid stories.");
                await tx.CommitAsync();
                return;
            }

            // ── FREEZE ORIGINAL PRIZE POOL ───────────────────────────────
            var reviewEndsAt = DateTime.UtcNow.AddHours(ev.ReviewHours);
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"UPDATE arena_events
                  SET original_prize_pool    = prize_pot_live,
                      status                 = 'review'::arena_event_status,
                      review_phase_ends_at   = @rEnd,
                      updated_at             = NOW()
                  WHERE id = @id",
                new Dictionary<string, object?>
                {
                    ["id"]   = ev.Id,
                    ["rEnd"] = reviewEndsAt,
                }, tx);

            // Deduct defaulters' entry fees → forfeit pool
            var defaulterCount = await DbHelper.ExecuteScalarAsync<int>(conn,
                "SELECT COUNT(*) FROM arena_participants WHERE event_id = @eid AND status = 'defaulted'",
                new Dictionary<string, object?> { ["eid"] = eventId }, tx);
            long forfeitPool = defaulterCount * ev.Fee;
            if (forfeitPool > 0)
            {
                await DbHelper.ExecuteNonQueryAsync(conn,
                    "UPDATE arena_events SET forfeit_pool = @fp, updated_at = NOW() WHERE id = @id",
                    new Dictionary<string, object?> { ["fp"] = forfeitPool, ["id"] = ev.Id }, tx);
            }

            await tx.CommitAsync();

            // ── FAIR SHUFFLE STORY ASSIGNMENT ────────────────────────────
            // Done outside the main transaction to avoid long lock
            await AssignStoriesForReviewAsync(eventId);

            _logger.LogInformation("Event {EventId} transitioned to review phase. {Count} stories to review.",
                eventId, validSubmissions);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "TransitionToReviewPhase failed for event {EventId}", eventId);
        }
    }

    /// <summary>
    /// Fair shuffle assignment:
    /// total_reviews_needed = submitted_stories × ReviewsPerStory
    /// base_load = total / participants
    /// remainder  = total % participants
    /// Randomly chosen 'remainder' participants get one extra review each.
    /// No reviewer reviews the same story twice — ever (UNIQUE enforced by DB).
    /// No self-review.
    /// </summary>
    private async Task AssignStoriesForReviewAsync(Guid eventId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        // Fetch all locked stories
        var stories = await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT id AS story_id, author_id
              FROM arena_stories WHERE event_id = @eid AND status = 'locked' ORDER BY id",
            r => new StoryParticipantInfo
            {
                StoryId  = DbHelper.GetGuid(r, "story_id"),
                AuthorId = DbHelper.GetGuid(r, "author_id"),
            },
            new Dictionary<string, object?> { ["eid"] = eventId });

        // Fetch active participants
        var participants = await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT user_id FROM arena_participants
              WHERE event_id = @eid AND status = 'active' ORDER BY user_id",
            r => new ParticipantInfo
            {
                UserId          = DbHelper.GetGuid(r, "user_id"),
                ReviewsAssigned = 0,
            },
            new Dictionary<string, object?> { ["eid"] = eventId });

        if (stories.Count == 0 || participants.Count == 0) return;

        int totalReviewsNeeded = stories.Count * ReviewsPerStory;
        int baseLoad           = totalReviewsNeeded / participants.Count;
        int remainder          = totalReviewsNeeded % participants.Count;

        // Randomly assign extra +1 review load to 'remainder' participants
        var rng = new Random();
        var shuffledParticipants = participants.OrderBy(_ => rng.Next()).ToList();
        for (int i = 0; i < shuffledParticipants.Count; i++)
        {
            shuffledParticipants[i].ReviewsAssigned = baseLoad + (i < remainder ? 1 : 0);
        }

        // Build assignment queue per story: need ReviewsPerStory reviewers each
        // Use round-robin with self-exclusion and lifetime uniqueness guard
        var assignments = new List<(Guid StoryId, Guid ReviewerId)>();
        var storyReviewers = new Dictionary<Guid, HashSet<Guid>>();

        foreach (var s in stories)
            storyReviewers[s.StoryId] = new HashSet<Guid>();

        // Build a pool of (reviewer, remaining_capacity) entries
        var reviewerPool = shuffledParticipants
            .SelectMany(p => Enumerable.Repeat(p.UserId, p.ReviewsAssigned))
            .OrderBy(_ => rng.Next())
            .ToList();

        // For each story, fill ReviewsPerStory slots — pick from pool avoiding self + already assigned
        foreach (var story in stories.OrderBy(_ => rng.Next()))
        {
            int assigned = 0;
            var tried    = new HashSet<int>();

            for (int idx = 0; idx < reviewerPool.Count && assigned < ReviewsPerStory; idx++)
            {
                var reviewerId = reviewerPool[idx];
                if (reviewerId == story.AuthorId) continue;
                if (storyReviewers[story.StoryId].Contains(reviewerId)) continue;

                storyReviewers[story.StoryId].Add(reviewerId);
                assignments.Add((story.StoryId, reviewerId));
                reviewerPool.RemoveAt(idx);
                idx--;
                assigned++;
            }
        }

        // Bulk insert assignments — UNIQUE(story_id, assigned_to) in DB handles races
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            foreach (var (storyId, reviewerId) in assignments)
            {
                try
                {
                    await DbHelper.ExecuteNonQueryAsync(conn,
                        @"INSERT INTO arena_review_assignments
                          (id, event_id, story_id, assigned_to, status, assigned_at, is_extra_review, extra_coins_offered)
                          SELECT gen_random_uuid(), ae.id, @sid, @rid, 'pending'::arena_assignment_status, NOW(), FALSE, 0
                          FROM arena_stories ars
                          JOIN arena_events ae ON ae.id = ars.event_id
                          WHERE ars.id = @sid
                          ON CONFLICT (story_id, assigned_to) DO NOTHING",
                        new Dictionary<string, object?> { ["sid"] = storyId, ["rid"] = reviewerId }, tx);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipping duplicate assignment story {StoryId} reviewer {ReviewerId}", storyId, reviewerId);
                }
            }
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        // Notify all active participants about review phase
        _ = Task.Run(async () =>
        {
            foreach (var p in participants)
            {
                await _notifications.CreateNotificationAsync(
                    p.UserId, "arena_review_started",
                    "Review Phase Shuru!",
                    "Stories assign ho gayi hain. Apne assignments check karo aur review karo!",
                    actionUrl: $"/arena/{eventId}/review");
            }
        });
    }

    /// <summary>
    /// Called by background job when review phase ends.
    /// 1. Mark any pending/in_progress assignments as defaulted.
    /// 2. Calculate average ratings for each submitted story.
    /// 3. Rank stories by avg_rating DESC, unique_reviewers DESC, random seed.
    /// 4. Split original_prize_pool: 30% platform, 35% rank1, 20% rank2, 15% rank3.
    /// 5. Credit coins to winners.
    /// 6. Award badges.
    /// 7. Mark event completed.
    /// </summary>
    public async Task FinalizeEventAsync(Guid eventId)
    {
        await using var conn = await _db.CreateConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            var ev = await DbHelper.ExecuteReaderFirstAsync(conn,
                @"SELECT id, status, original_prize_pool, title, random_seed
                  FROM arena_events WHERE id = @id FOR UPDATE",
                r => new
                {
                    Id           = DbHelper.GetGuid(r, "id"),
                    Status       = DbHelper.GetString(r, "status"),
                    PrizePool    = DbHelper.GetLong(r, "original_prize_pool"),
                    Title        = DbHelper.GetString(r, "title"),
                    RandomSeed   = r.IsDBNull(r.GetOrdinal("random_seed")) ? 0 : DbHelper.GetInt(r, "random_seed"),
                },
                new Dictionary<string, object?> { ["id"] = eventId }, tx);

            if (ev == null || ev.Status != "review")
            {
                _logger.LogWarning("FinalizeEvent: Event {EventId} not in review status", eventId);
                return;
            }

            // Mark defaulted assignments
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"UPDATE arena_review_assignments
                  SET status = 'defaulted'::arena_assignment_status, updated_at = NOW()
                  WHERE event_id = @eid AND status IN ('pending','in_progress')",
                new Dictionary<string, object?> { ["eid"] = eventId }, tx);

            // Calculate rankings — average rating of each locked story with >= 1 completed review
            // Tie-break 1: unique reviewers count DESC
            // Tie-break 2: random_seed (set once on event creation, deterministic)
            int seed = ev.RandomSeed == 0 ? new Random().Next() : ev.RandomSeed;
            if (ev.RandomSeed == 0)
            {
                await DbHelper.ExecuteNonQueryAsync(conn,
                    "UPDATE arena_events SET random_seed = @seed WHERE id = @id",
                    new Dictionary<string, object?> { ["seed"] = seed, ["id"] = ev.Id }, tx);
            }

            // Pull story ratings
            var storyRatings = await DbHelper.ExecuteReaderAsync(conn,
                @"SELECT ars.id AS story_id, ars.author_id,
                         AVG(ar.rating)::DECIMAL(10,4) AS avg_rating,
                         COUNT(DISTINCT ar.rated_by)   AS unique_reviewers
                  FROM arena_stories ars
                  JOIN arena_ratings ar ON ar.story_id = ars.id
                  WHERE ars.event_id = @eid AND ars.status = 'locked'
                  GROUP BY ars.id, ars.author_id",
                r => new
                {
                    StoryId         = DbHelper.GetGuid(r, "story_id"),
                    AuthorId        = DbHelper.GetGuid(r, "author_id"),
                    AvgRating       = DbHelper.GetDecimal(r, "avg_rating"),
                    UniqueReviewers = DbHelper.GetInt(r, "unique_reviewers"),
                },
                new Dictionary<string, object?> { ["eid"] = eventId }, tx);

            if (storyRatings.Count == 0)
            {
                // Nobody reviewed — cancel event, refund all
                _logger.LogWarning("FinalizeEvent: No ratings found for event {EventId} — cancelling", eventId);
                await CancelDueToThresholdAsync(conn, tx, ev.Id, 0, "No reviews completed during review phase.");
                await tx.CommitAsync();
                return;
            }

            // Deterministic sort with random seed as final tie-break
            var rng = new Random(seed);
            var ranked = storyRatings
                .OrderByDescending(s => s.AvgRating)
                .ThenByDescending(s => s.UniqueReviewers)
                .ThenBy(_ => rng.NextDouble())
                .ToList();

            // Revenue split from ORIGINAL prize pool only
            long platformCut = (long)(ev.PrizePool * PlatformCutPercent);
            long rank1Prize  = (long)(ev.PrizePool * Rank1CutPercent);
            long rank2Prize  = ranked.Count >= 2 ? (long)(ev.PrizePool * Rank2CutPercent) : 0;
            long rank3Prize  = ranked.Count >= 3 ? (long)(ev.PrizePool * Rank3CutPercent) : 0;

            // If fewer than 3 winners — redistribute remaining to rank1
            long undistributed = ev.PrizePool - platformCut - rank1Prize - rank2Prize - rank3Prize;
            rank1Prize += undistributed;

            var winnerPrizes = new long[] { rank1Prize, rank2Prize, rank3Prize };
            var now = DateTime.UtcNow;

            for (int rank = 0; rank < Math.Min(ranked.Count, 3); rank++)
            {
                var story = ranked[rank];
                long prize = winnerPrizes[rank];
                int rankNum = rank + 1;

                // Record winner
                await DbHelper.ExecuteNonQueryAsync(conn,
                    @"INSERT INTO arena_winners
                      (id, event_id, user_id, story_id, rank, average_rating, total_reviews, coins_won, awarded_at)
                      VALUES (gen_random_uuid(), @eid, @uid, @sid, @rank, @avgR, @reviews, @coins, @now)",
                    new Dictionary<string, object?>
                    {
                        ["eid"]     = ev.Id,
                        ["uid"]     = story.AuthorId,
                        ["sid"]     = story.StoryId,
                        ["rank"]    = rankNum,
                        ["avgR"]    = story.AvgRating,
                        ["reviews"] = story.UniqueReviewers,
                        ["coins"]   = prize,
                        ["now"]     = now,
                    }, tx);

                // Credit coins to winner
                await DbHelper.ExecuteNonQueryAsync(conn,
                    @"UPDATE wallets
                      SET coin_balance = coin_balance + @coins,
                          total_earned = total_earned + @coins,
                          last_transaction_at = NOW(), updated_at = NOW()
                      WHERE user_id = @uid",
                    new Dictionary<string, object?> { ["coins"] = prize, ["uid"] = story.AuthorId }, tx);

                // Transaction record
                await DbHelper.ExecuteNonQueryAsync(conn,
                    @"INSERT INTO coin_transactions
                      (id, sender_id, receiver_id, amount, type, description, status, completed_at)
                      VALUES (gen_random_uuid(), NULL, @uid, @coins, 'arena_prize',
                              'Darr Arena Rank ' || @rank || ' prize — ' || @title, 'completed', @now)",
                    new Dictionary<string, object?>
                    {
                        ["uid"]   = story.AuthorId,
                        ["coins"] = prize,
                        ["rank"]  = rankNum,
                        ["title"] = ev.Title,
                        ["now"]   = now,
                    }, tx);

                // Award badge
                string badgeType = rankNum switch { 1 => "champion", 2 => "runner_up", _ => "second_runner_up" };
                await DbHelper.ExecuteNonQueryAsync(conn,
                    @"INSERT INTO arena_badges
                      (id, event_id, user_id, story_id, event_title, badge_type, rank, coins_won, awarded_at)
                      VALUES (gen_random_uuid(), @eid, @uid, @sid, @etitle, @bt::arena_badge_type, @rank, @coins, @now)",
                    new Dictionary<string, object?>
                    {
                        ["eid"]    = ev.Id,
                        ["uid"]    = story.AuthorId,
                        ["sid"]    = story.StoryId,
                        ["etitle"] = ev.Title,
                        ["bt"]     = badgeType,
                        ["rank"]   = rankNum,
                        ["coins"]  = prize,
                        ["now"]    = now,
                    }, tx);
            }

            // Finalize event
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"UPDATE arena_events
                  SET status = 'completed'::arena_event_status,
                      platform_cut = @cut,
                      completed_at = @now,
                      updated_at   = @now
                  WHERE id = @id",
                new Dictionary<string, object?>
                {
                    ["cut"] = platformCut,
                    ["now"] = now,
                    ["id"]  = ev.Id,
                }, tx);

            await tx.CommitAsync();

            // Notify winners (new connection — outer conn/tx already committed)
            var capturedTitle = ev.Title;
            _ = Task.Run(async () =>
            {
                await using var notifConn = await _db.CreateConnectionAsync();
                var winnerList = await GetEventWinnersAsync(notifConn, eventId);
                foreach (var w in winnerList)
                {
                    await _notifications.CreateNotificationAsync(
                        w.UserId, "arena_winner",
                        $"Rank #{w.Rank} — Darr Arena Jeet Gaye!",
                        $"Tumne '{capturedTitle}' mein Rank #{w.Rank} haasil kiya! {w.CoinsWon} coins tumhare wallet mein aa gaye.",
                        actionUrl: $"/arena/{eventId}/results");
                }
            });

            _logger.LogInformation("Event {EventId} finalized. {Count} winners crowned.", eventId, Math.Min(ranked.Count, 3));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "FinalizeEvent failed for event {EventId}", eventId);
        }
    }

    /// <summary>
    /// Runs every 30 min during review phase.
    /// Detects participants who have been assigned stories but haven't started reviewing.
    /// Redistributes their assignments from the forfeit pool with extra coin incentives.
    /// </summary>
    public async Task RunForfeitDetectionAsync(Guid eventId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        var ev = await DbHelper.ExecuteReaderFirstAsync(conn,
            @"SELECT id, status, review_phase_ends_at, forfeit_pool
              FROM arena_events WHERE id = @id AND deleted_at IS NULL",
            r => new
            {
                Id              = DbHelper.GetGuid(r, "id"),
                Status          = DbHelper.GetString(r, "status"),
                ReviewEndsAt    = DbHelper.GetDateTime(r, "review_phase_ends_at"),
                ForfeitPool     = DbHelper.GetLong(r, "forfeit_pool"),
            },
            new Dictionary<string, object?> { ["id"] = eventId });

        if (ev == null || ev.Status != "review") return;

        // Find stories that still need more completed reviews
        var understaffedStories = await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT ars.id AS story_id,
                     COUNT(DISTINCT ara.id) FILTER (WHERE ara.status = 'completed') AS completed_reviews,
                     COUNT(DISTINCT ara.id) FILTER (WHERE ara.status IN ('pending','in_progress')) AS pending_reviews
              FROM arena_stories ars
              LEFT JOIN arena_review_assignments ara ON ara.story_id = ars.id
              WHERE ars.event_id = @eid AND ars.status = 'locked'
              GROUP BY ars.id
              HAVING COUNT(DISTINCT ara.id) FILTER (WHERE ara.status = 'completed') < @target",
            r => new
            {
                StoryId          = DbHelper.GetGuid(r, "story_id"),
                CompletedReviews = DbHelper.GetInt(r, "completed_reviews"),
                PendingReviews   = DbHelper.GetInt(r, "pending_reviews"),
            },
            new Dictionary<string, object?> { ["eid"] = eventId, ["target"] = ReviewsPerStory });

        if (understaffedStories.Count == 0) return;

        // Find active participants who have capacity for extra reviews
        // Exclude those who already have pending assignments for this event
        var availableReviewers = await DbHelper.ExecuteReaderAsync<Guid>(conn,
            @"SELECT ap.user_id
              FROM arena_participants ap
              WHERE ap.event_id = @eid AND ap.status = 'active'
                AND NOT EXISTS (
                    SELECT 1 FROM arena_review_assignments ara2
                    WHERE ara2.event_id = @eid
                      AND ara2.assigned_to = ap.user_id
                      AND ara2.status IN ('pending','in_progress')
                      AND ara2.is_extra_review = TRUE
                )
              LIMIT 50",
            r => DbHelper.GetGuid(r, "user_id"),
            new Dictionary<string, object?> { ["eid"] = eventId });

        if (availableReviewers.Count == 0 || ev.ForfeitPool <= 0) return;

        long remainingForfeitPool = ev.ForfeitPool;
        var rng = new Random();
        var shuffledReviewers = availableReviewers.OrderBy(_ => rng.Next()).ToList();
        int reviewerIdx = 0;

        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            foreach (var story in understaffedStories)
            {
                int needed = ReviewsPerStory - story.CompletedReviews - story.PendingReviews;
                if (needed <= 0) continue;
                if (remainingForfeitPool <= 0) break;

                for (int i = 0; i < needed && reviewerIdx < shuffledReviewers.Count; i++)
                {
                    var reviewerId = shuffledReviewers[reviewerIdx++];

                    // Skip if this reviewer is the author
                    var isAuthor = await DbHelper.ExecuteScalarAsync<bool>(conn,
                        "SELECT EXISTS(SELECT 1 FROM arena_stories WHERE id = @sid AND author_id = @uid)",
                        new Dictionary<string, object?> { ["sid"] = story.StoryId, ["uid"] = reviewerId }, tx);
                    if (isAuthor) continue;

                    // Check UNIQUE constraint — skip if already assigned (ever)
                    var alreadyAssigned = await DbHelper.ExecuteScalarAsync<bool>(conn,
                        "SELECT EXISTS(SELECT 1 FROM arena_review_assignments WHERE story_id = @sid AND assigned_to = @rid)",
                        new Dictionary<string, object?> { ["sid"] = story.StoryId, ["rid"] = reviewerId }, tx);
                    if (alreadyAssigned) continue;

                    int extraCoins = (int)Math.Min(ExtraCoinsPerForfeitReview, remainingForfeitPool);
                    remainingForfeitPool -= extraCoins;

                    await DbHelper.ExecuteNonQueryAsync(conn,
                        @"INSERT INTO arena_review_assignments
                          (id, event_id, story_id, assigned_to, status, assigned_at, is_extra_review, extra_coins_offered)
                          SELECT gen_random_uuid(), ae.id, @sid, @rid, 'pending'::arena_assignment_status,
                                 NOW(), TRUE, @extra
                          FROM arena_stories ars
                          JOIN arena_events ae ON ae.id = ars.event_id
                          WHERE ars.id = @sid
                          ON CONFLICT (story_id, assigned_to) DO NOTHING",
                        new Dictionary<string, object?>
                        {
                            ["sid"]   = story.StoryId,
                            ["rid"]   = reviewerId,
                            ["extra"] = extraCoins,
                        }, tx);

                    // Notify reviewer
                    _ = _notifications.CreateNotificationAsync(
                        reviewerId, "arena_extra_review_offer",
                        "Bonus Review Offer!",
                        $"Ek aur story review karo aur {extraCoins} bonus coins kamao! Forfeit pool se mil raha hai.",
                        actionUrl: $"/arena/{eventId}/review");
                }
            }

            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "ForfeitDetection failed for event {EventId}", eventId);
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    //  PRIVATE HELPERS
    // ───────────────────────────────────────────────────────────────────────

    private static ArenaEventResponse MapArenaEvent(NpgsqlDataReader r, bool hasViewerContext)
    {
        return new ArenaEventResponse
        {
            Id                        = DbHelper.GetGuid(r, "id"),
            Title                     = DbHelper.GetString(r, "title"),
            Description               = DbHelper.GetString(r, "description"),
            Topic                     = DbHelper.GetString(r, "topic"),
            StoryType                 = DbHelper.GetString(r, "story_type"),
            MinWordLimit              = DbHelper.GetInt(r, "min_word_limit"),
            MaxWordLimit              = DbHelper.GetInt(r, "max_word_limit"),
            EntryFeeCoins             = DbHelper.GetInt(r, "entry_fee_coins"),
            HallReadingCostCoins      = DbHelper.GetInt(r, "hall_reading_cost_coins"),
            WritingPhaseHours         = DbHelper.GetInt(r, "writing_phase_hours"),
            ReviewPhaseHours          = DbHelper.GetInt(r, "review_phase_hours"),
            MinParticipantsThreshold  = DbHelper.GetInt(r, "min_participants_threshold"),
            Status                    = DbHelper.GetString(r, "status"),
            OriginalPrizePool         = DbHelper.GetLong(r, "original_prize_pool"),
            PrizePotLive              = DbHelper.GetLong(r, "prize_pot_live"),
            TotalParticipants         = DbHelper.GetInt(r, "total_participants"),
            TotalSubmitted            = DbHelper.GetInt(r, "total_submitted"),
            WritingPhaseStartsAt      = DbHelper.GetDateTimeOrNull(r, "writing_phase_starts_at"),
            WritingPhaseEndsAt        = DbHelper.GetDateTimeOrNull(r, "writing_phase_ends_at"),
            ReviewPhaseEndsAt         = DbHelper.GetDateTimeOrNull(r, "review_phase_ends_at"),
            CompletedAt               = DbHelper.GetDateTimeOrNull(r, "completed_at"),
            CancellationReason        = DbHelper.GetStringOrNull(r, "cancellation_reason"),
            CreatedByUsername         = DbHelper.GetString(r, "created_by_username"),
            CreatedAt                 = DbHelper.GetDateTime(r, "created_at"),
            HasJoined                 = hasViewerContext ? (bool?)DbHelper.GetBool(r, "has_joined") : null,
            HasSubmitted              = hasViewerContext ? (bool?)DbHelper.GetBool(r, "has_submitted") : null,
        };
    }

    private static async Task<List<QuestionPreviewDto>> GetStoryQuestionsAsync(
        NpgsqlConnection conn, Guid storyId, bool includeCorrect)
    {
        return await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT question_order, question_text, option_a, option_b, option_c, option_d
              FROM arena_story_questions WHERE story_id = @sid ORDER BY question_order",
            r => new QuestionPreviewDto
            {
                Order        = DbHelper.GetInt(r, "question_order"),
                QuestionText = DbHelper.GetString(r, "question_text"),
                OptionA      = DbHelper.GetString(r, "option_a"),
                OptionB      = DbHelper.GetString(r, "option_b"),
                OptionC      = DbHelper.GetString(r, "option_c"),
                OptionD      = DbHelper.GetString(r, "option_d"),
            },
            new Dictionary<string, object?> { ["sid"] = storyId });
    }

    private async Task<List<ArenaWinnerResponse>> GetEventWinnersAsync(NpgsqlConnection conn, Guid eventId)
    {
        return await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT aw.rank, aw.user_id, u.username, u.display_name, u.avatar_url,
                     aw.story_id, ars.title AS story_title,
                     aw.average_rating, aw.total_reviews, aw.coins_won
              FROM arena_winners aw
              JOIN users u ON u.id = aw.user_id
              JOIN arena_stories ars ON ars.id = aw.story_id
              WHERE aw.event_id = @eid ORDER BY aw.rank ASC",
            r => new ArenaWinnerResponse
            {
                Rank          = DbHelper.GetInt(r, "rank"),
                UserId        = DbHelper.GetGuid(r, "user_id"),
                Username      = DbHelper.GetString(r, "username"),
                DisplayName   = DbHelper.GetStringOrNull(r, "display_name"),
                AvatarUrl     = DbHelper.GetStringOrNull(r, "avatar_url"),
                StoryId       = DbHelper.GetGuid(r, "story_id"),
                StoryTitle    = DbHelper.GetString(r, "story_title"),
                AverageRating = DbHelper.GetDecimal(r, "average_rating"),
                TotalReviews  = DbHelper.GetInt(r, "total_reviews"),
                CoinsWon      = DbHelper.GetLong(r, "coins_won"),
            },
            new Dictionary<string, object?> { ["eid"] = eventId });
    }

    private async Task<List<ArenaLeaderboardEntry>> GetLeaderboardAsync(NpgsqlConnection conn, Guid eventId)
    {
        return await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT ROW_NUMBER() OVER (ORDER BY avg_r.avg_rating DESC, avg_r.unique_reviewers DESC) AS position,
                     ars.author_id AS user_id, u.username, u.avatar_url,
                     ars.title AS story_title,
                     COALESCE(avg_r.avg_rating, 0) AS average_rating,
                     COALESCE(avg_r.unique_reviewers, 0) AS total_reviews,
                     COALESCE(aw.coins_won, 0) AS coins_won,
                     ap.status
              FROM arena_stories ars
              JOIN users u ON u.id = ars.author_id
              JOIN arena_participants ap ON ap.event_id = ars.event_id AND ap.user_id = ars.author_id
              LEFT JOIN arena_winners aw ON aw.event_id = ars.event_id AND aw.user_id = ars.author_id
              LEFT JOIN LATERAL (
                  SELECT AVG(ar.rating)::DECIMAL(5,2) AS avg_rating,
                         COUNT(DISTINCT ar.rated_by)  AS unique_reviewers
                  FROM arena_ratings ar WHERE ar.story_id = ars.id
              ) avg_r ON TRUE
              WHERE ars.event_id = @eid AND ars.status = 'locked'
              ORDER BY average_rating DESC, total_reviews DESC",
            r => new ArenaLeaderboardEntry
            {
                Position      = DbHelper.GetInt(r, "position"),
                UserId        = DbHelper.GetGuid(r, "user_id"),
                Username      = DbHelper.GetString(r, "username"),
                AvatarUrl     = DbHelper.GetStringOrNull(r, "avatar_url"),
                StoryTitle    = DbHelper.GetString(r, "story_title"),
                AverageRating = DbHelper.GetDecimal(r, "average_rating"),
                TotalReviews  = DbHelper.GetInt(r, "total_reviews"),
                CoinsWon      = DbHelper.GetLong(r, "coins_won"),
                Status        = DbHelper.GetString(r, "status"),
            },
            new Dictionary<string, object?> { ["eid"] = eventId });
    }

    /// <summary>
    /// Cancel event due to insufficient threshold or no reviews.
    /// Refunds all active participants' entry fees.
    /// </summary>
    private async Task CancelDueToThresholdAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid eventId, int fee, string reason)
    {
        if (fee > 0)
        {
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"UPDATE wallets w
                  SET coin_balance = coin_balance + @fee,
                      total_earned = total_earned + @fee,
                      updated_at   = NOW()
                  FROM arena_participants ap
                  WHERE ap.event_id = @eid AND ap.user_id = w.user_id
                    AND ap.status NOT IN ('refunded', 'disqualified')",
                new Dictionary<string, object?> { ["fee"] = fee, ["eid"] = eventId }, tx);

            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO coin_transactions (id, sender_id, receiver_id, amount, type, description, status, completed_at)
                  SELECT gen_random_uuid(), NULL, ap.user_id, @fee, 'arena_refund',
                         'Arena auto-cancelled: ' || @reason, 'completed', NOW()
                  FROM arena_participants ap
                  WHERE ap.event_id = @eid AND ap.status NOT IN ('refunded', 'disqualified')",
                new Dictionary<string, object?> { ["fee"] = fee, ["eid"] = eventId, ["reason"] = reason }, tx);
        }

        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE arena_participants SET status = 'refunded'::arena_participant_status, updated_at = NOW() WHERE event_id = @eid AND status NOT IN ('refunded')",
            new Dictionary<string, object?> { ["eid"] = eventId }, tx);

        await DbHelper.ExecuteNonQueryAsync(conn,
            @"UPDATE arena_events
              SET status = 'cancelled'::arena_event_status,
                  cancellation_reason = @reason,
                  updated_at = NOW()
              WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = eventId, ["reason"] = reason }, tx);
    }

    /// <summary>
    /// Simple server-side word count — split on whitespace, ignore empties.
    /// </summary>
    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Split(new[] { ' ', '\t', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
