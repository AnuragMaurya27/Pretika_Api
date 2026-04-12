using HauntedVoiceUniverse.Infrastructure.Database;
using HauntedVoiceUniverse.Modules.Arena.Services;

namespace HauntedVoiceUniverse.Modules.Arena.Jobs;

// ═══════════════════════════════════════════════════════════════════════════
//  ARENA FORFEIT DETECTION JOB
//  Polls every 30 minutes during the review phase.
//  For each active review-phase event:
//    • Finds stories that are short on completed reviews.
//    • Redistributes them to available participants with extra coin incentives
//      drawn from the forfeit pool.
// ═══════════════════════════════════════════════════════════════════════════

public class ArenaForfeitDetectionJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ArenaForfeitDetectionJob> _logger;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMinutes(30);

    public ArenaForfeitDetectionJob(
        IServiceScopeFactory scopeFactory,
        ILogger<ArenaForfeitDetectionJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ArenaForfeitDetectionJob started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ArenaForfeitDetectionJob encountered an error");
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

        // All events currently in review phase that still have forfeit pool coins
        var activeReviewEvents = await DbHelper.ExecuteReaderAsync<Guid>(conn,
            @"SELECT id FROM arena_events
              WHERE status = 'review'::arena_event_status
                AND forfeit_pool > 0
                AND review_phase_ends_at > NOW()   -- still time left
                AND deleted_at IS NULL
              LIMIT 20",
            r => DbHelper.GetGuid(r, "id"));

        foreach (var eventId in activeReviewEvents)
        {
            if (ct.IsCancellationRequested) return;
            _logger.LogDebug("Running forfeit detection for event {EventId}", eventId);
            await arena.RunForfeitDetectionAsync(eventId);
        }
    }
}
