using HauntedVoiceUniverse.Infrastructure.Database;
using HauntedVoiceUniverse.Modules.Arena.Services;

namespace HauntedVoiceUniverse.Modules.Arena.Jobs;

// ═══════════════════════════════════════════════════════════════════════════
//  ARENA PHASE TRANSITION JOB
//  Polls every 60 seconds. Finds events whose writing/review phase has just
//  ended and transitions them to the next phase.
//
//  Writing ends → TransitionToReviewPhaseAsync (assigns stories)
//  Review ends  → FinalizeEventAsync (calc winners, pay prizes, award badges)
// ═══════════════════════════════════════════════════════════════════════════

public class ArenaPhaseTransitionJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ArenaPhaseTransitionJob> _logger;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(60);

    public ArenaPhaseTransitionJob(
        IServiceScopeFactory scopeFactory,
        ILogger<ArenaPhaseTransitionJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ArenaPhaseTransitionJob started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ArenaPhaseTransitionJob encountered an error");
            }

            await Task.Delay(PollingInterval, stoppingToken);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db    = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        var arena = scope.ServiceProvider.GetRequiredService<IArenaService>();

        await using var conn = await db.CreateConnectionAsync();

        // ── 1. Writing phase just ended → transition to review ──────────────
        var writingExpired = await DbHelper.ExecuteReaderAsync<Guid>(conn,
            @"SELECT id FROM arena_events
              WHERE status = 'writing'::arena_event_status
                AND writing_phase_ends_at <= NOW()
                AND deleted_at IS NULL
              LIMIT 10",
            r => DbHelper.GetGuid(r, "id"));

        foreach (var eventId in writingExpired)
        {
            if (ct.IsCancellationRequested) return;
            _logger.LogInformation("Writing phase ended for event {EventId} — transitioning to review", eventId);
            await arena.TransitionToReviewPhaseAsync(eventId);
        }

        // ── 2. Review phase just ended → finalize, pick winners ─────────────
        var reviewExpired = await DbHelper.ExecuteReaderAsync<Guid>(conn,
            @"SELECT id FROM arena_events
              WHERE status = 'review'::arena_event_status
                AND review_phase_ends_at <= NOW()
                AND deleted_at IS NULL
              LIMIT 10",
            r => DbHelper.GetGuid(r, "id"));

        foreach (var eventId in reviewExpired)
        {
            if (ct.IsCancellationRequested) return;
            _logger.LogInformation("Review phase ended for event {EventId} — finalizing winners", eventId);
            await arena.FinalizeEventAsync(eventId);
        }

        // ── 3. Upcoming events → activate writing phase if start time reached ─
        var nowActivated = await DbHelper.ExecuteReaderAsync<Guid>(conn,
            @"SELECT id FROM arena_events
              WHERE status = 'upcoming'::arena_event_status
                AND writing_phase_starts_at <= NOW()
                AND deleted_at IS NULL
              LIMIT 10",
            r => DbHelper.GetGuid(r, "id"));

        if (nowActivated.Count > 0)
        {
            // Batch update upcoming → writing
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"UPDATE arena_events
                  SET status = 'writing'::arena_event_status, updated_at = NOW()
                  WHERE status = 'upcoming'::arena_event_status
                    AND writing_phase_starts_at <= NOW()
                    AND deleted_at IS NULL");

            foreach (var eventId in nowActivated)
                _logger.LogInformation("Event {EventId} writing phase has started", eventId);
        }
    }
}
