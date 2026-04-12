using System.Collections.Concurrent;
using System.Security.Claims;

namespace HauntedVoiceUniverse.Infrastructure.Middleware;

/// <summary>
/// VULN#15 FIX: Per-authenticated-user rate limiting.
/// AspNetCoreRateLimit is IP-based only, which fails for:
///   - Multiple accounts sharing one IP (NAT/VPN) — they share the IP limit pool.
///   - Authenticated endpoints where IP is irrelevant (JWT identifies the user).
/// This middleware enforces per-user limits on sensitive endpoints AFTER auth is resolved.
///
/// Current limits (configurable via appsettings):
///   - /api/wallet/appreciate        → 20 requests per 60 seconds
///   - /api/wallet/recharge/verify   → 10 requests per 60 seconds
///   - /api/users/*/follow           → 60 requests per 60 seconds
///   - /api/auth/refresh             → 20 requests per 60 seconds
/// </summary>
public class UserRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UserRateLimitMiddleware> _logger;

    // (userId, endpoint-prefix) → list of request timestamps
    private static readonly ConcurrentDictionary<string, Queue<DateTime>> _windows = new();

    // Cleanup old entries every 5 minutes
    private static DateTime _lastCleanup = DateTime.UtcNow;

    private static readonly List<(string PathPrefix, int MaxRequests, int WindowSeconds)> _rules = new()
    {
        ("/api/wallet/appreciate",      20, 60),
        ("/api/wallet/recharge/verify", 10, 60),
        ("/api/users/",                 60, 60),   // follow/unfollow/block
        ("/api/auth/refresh",           20, 60),
        ("/api/chat/rooms/",            30, 60),   // join/leave/send
    };

    public UserRateLimitMiddleware(RequestDelegate next, ILogger<UserRateLimitMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var userId = context.User?.FindFirstValue("uid");
        if (!string.IsNullOrEmpty(userId) && context.User?.Identity?.IsAuthenticated == true)
        {
            var path = context.Request.Path.Value?.ToLower() ?? "";
            var method = context.Request.Method;

            // Only apply to state-changing requests
            if (method is "POST" or "PUT" or "PATCH" or "DELETE")
            {
                foreach (var (prefix, maxRequests, windowSeconds) in _rules)
                {
                    if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var key = $"{userId}:{prefix}";
                        var now = DateTime.UtcNow;
                        var cutoff = now.AddSeconds(-windowSeconds);

                        var queue = _windows.GetOrAdd(key, _ => new Queue<DateTime>());
                        bool rateLimited;
                        lock (queue)
                        {
                            // Evict old entries outside window
                            while (queue.Count > 0 && queue.Peek() < cutoff)
                                queue.Dequeue();

                            rateLimited = queue.Count >= maxRequests;
                            if (!rateLimited)
                                queue.Enqueue(now);
                        }

                        if (rateLimited)
                        {
                            _logger.LogWarning(
                                "User rate limit exceeded: userId={UserId}, path={Path}",
                                userId, path);
                            context.Response.StatusCode = 429;
                            context.Response.Headers.Append("Retry-After", windowSeconds.ToString());
                            await context.Response.WriteAsJsonAsync(new
                            {
                                success = false,
                                message = $"Too many requests. {windowSeconds} seconds baad try karo."
                            });
                            return;
                        }
                        break; // Only apply first matching rule
                    }
                }
            }

            // Periodic cleanup to prevent memory growth
            if ((DateTime.UtcNow - _lastCleanup).TotalMinutes > 5)
            {
                _lastCleanup = DateTime.UtcNow;
                var cutoffGlobal = DateTime.UtcNow.AddMinutes(-10);
                foreach (var kvp in _windows)
                {
                    lock (kvp.Value)
                    {
                        while (kvp.Value.Count > 0 && kvp.Value.Peek() < cutoffGlobal)
                            kvp.Value.Dequeue();
                    }
                    if (kvp.Value.Count == 0)
                        _windows.TryRemove(kvp.Key, out _);
                }
            }
        }

        await _next(context);
    }
}
