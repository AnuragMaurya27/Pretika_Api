using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using HauntedVoiceUniverse.Infrastructure.Database;

namespace HauntedVoiceUniverse.Infrastructure.Push;

public interface IFcmService
{
    Task SendToUserAsync(Guid userId, string title, string body, string notificationType,
        Dictionary<string, string>? extraData = null);
}

public class FcmService : IFcmService
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<FcmService> _logger;
    private readonly bool _enabled;

    public FcmService(IDbConnectionFactory db, ILogger<FcmService> logger, IConfiguration config)
    {
        _db = db;
        _logger = logger;

        var serviceAccountPath = config["Firebase:ServiceAccountPath"];
        if (string.IsNullOrEmpty(serviceAccountPath) || !File.Exists(serviceAccountPath))
        {
            _enabled = false;
            return;
        }

        try
        {
            if (FirebaseApp.DefaultInstance == null)
            {
                FirebaseApp.Create(new AppOptions
                {
                    Credential = GoogleCredential.FromFile(serviceAccountPath)
                });
            }
            _enabled = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Firebase init failed — push notifications disabled");
            _enabled = false;
        }
    }

    public async Task SendToUserAsync(Guid userId, string title, string body, string notificationType,
        Dictionary<string, string>? extraData = null)
    {
        if (!_enabled) return;

        try
        {
            await using var conn = await _db.CreateConnectionAsync();
            var tokens = await DbHelper.ExecuteReaderAsync(conn,
                "SELECT device_token FROM user_devices WHERE user_id = @uid AND is_active = TRUE",
                r => r.GetString(0),
                new Dictionary<string, object?> { ["uid"] = userId });

            if (tokens.Count == 0) return;

            var data = new Dictionary<string, string>
            {
                ["notification_type"] = notificationType
            };
            if (extraData != null)
                foreach (var kv in extraData) data[kv.Key] = kv.Value;

            var messaging = FirebaseMessaging.DefaultInstance;

            // Send in chunks of 500 (FCM multicast limit)
            foreach (var chunk in tokens.Chunk(500))
            {
                var message = new MulticastMessage
                {
                    Tokens = chunk.ToList(),
                    Notification = new Notification { Title = title, Body = body },
                    Data = data,
                    Android = new AndroidConfig
                    {
                        Priority = notificationType == "message"
                            ? Priority.High
                            : Priority.Normal,
                        Notification = new AndroidNotification
                        {
                            ChannelId = notificationType == "message"
                                ? "pretika_messages"
                                : "pretika_main",
                            Icon = "@mipmap/ic_launcher",
                            Color = "#CC0000"
                        }
                    }
                };

                var response = await messaging.SendEachForMulticastAsync(message);

                // Deactivate stale tokens reported by FCM
                var staleTokens = new List<string>();
                for (int i = 0; i < response.Responses.Count; i++)
                {
                    var r = response.Responses[i];
                    if (!r.IsSuccess &&
                        (r.Exception?.MessagingErrorCode == MessagingErrorCode.Unregistered ||
                         r.Exception?.MessagingErrorCode == MessagingErrorCode.InvalidArgument))
                    {
                        staleTokens.Add(chunk[i]);
                    }
                }

                if (staleTokens.Count > 0)
                {
                    _ = DeactivateStaleTokensAsync(staleTokens);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FCM send failed for user {UserId}", userId);
        }
    }

    private async Task DeactivateStaleTokensAsync(List<string> tokens)
    {
        try
        {
            await using var conn = await _db.CreateConnectionAsync();
            foreach (var token in tokens)
            {
                await DbHelper.ExecuteNonQueryAsync(conn,
                    "UPDATE user_devices SET is_active = FALSE WHERE device_token = @token",
                    new Dictionary<string, object?> { ["token"] = token });
            }
        }
        catch { }
    }
}
