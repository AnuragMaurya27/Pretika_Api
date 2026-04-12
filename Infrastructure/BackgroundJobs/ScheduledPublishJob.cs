using HauntedVoiceUniverse.Infrastructure.Database;

namespace HauntedVoiceUniverse.Infrastructure.BackgroundJobs;

// BUG#M3-8 FIX: Scheduled publishing was completely unimplemented.
// Episodes with scheduled_publish_at stayed 'draft' forever because no
// background process ever checked and promoted them.
//
// This IHostedService runs every 60 seconds, finds episodes whose
// scheduled_publish_at <= NOW() and status = 'draft', and publishes them.
// Also checks that the story's creator is not banned/deleted before publishing.

public class ScheduledPublishJob : BackgroundService
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<ScheduledPublishJob> _logger;
    private static readonly TimeSpan _interval = TimeSpan.FromSeconds(60);

    public ScheduledPublishJob(IDbConnectionFactory db, ILogger<ScheduledPublishJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScheduledPublishJob started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishDueEpisodesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ScheduledPublishJob error: {Msg}", ex.Message);
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task PublishDueEpisodesAsync(CancellationToken ct)
    {
        await using var conn = await _db.CreateConnectionAsync();

        // BUG#M3-23/24 FIX: Do NOT publish episodes whose creator is banned or deleted.
        // If creator account is deleted/banned before scheduled time, episode stays draft.
        var rows = await DbHelper.ExecuteNonQueryAsync(conn, @"
            UPDATE episodes e
            SET    status = 'published'::story_status,
                   published_at = NOW(),
                   updated_at   = NOW()
            FROM   stories s
            JOIN   users   u ON u.id = s.creator_id
            WHERE  e.story_id = s.id
              AND  e.status = 'draft'::story_status
              AND  e.deleted_at IS NULL
              AND  e.scheduled_publish_at IS NOT NULL
              AND  e.scheduled_publish_at <= NOW()
              AND  s.deleted_at IS NULL
              AND  u.deleted_at IS NULL
              AND  u.status NOT IN ('banned', 'suspended')",
            new());

        if (rows > 0)
            _logger.LogInformation("ScheduledPublishJob: published {Count} episode(s).", rows);
    }
}
