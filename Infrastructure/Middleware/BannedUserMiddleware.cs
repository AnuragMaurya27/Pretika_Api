using HauntedVoiceUniverse.Infrastructure.Database;
using System.Security.Claims;
using System.Text.Json;

namespace HauntedVoiceUniverse.Infrastructure.Middleware;

/// <summary>
/// BUG#A5 FIX: JWT stays valid after ban.
/// This middleware checks the user's account status in DB on every authenticated request.
/// If the user is banned/suspended, it returns 403 immediately — even with a valid JWT.
///
/// Skips admin roles so admins can still operate even if status changes are in flight.
/// Skips unauthenticated requests (no JWT) — those are handled by [Authorize].
/// </summary>
public class BannedUserMiddleware
{
    private readonly RequestDelegate _next;

    public BannedUserMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IDbConnectionFactory db)
    {
        // Only check authenticated requests
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var role = context.User.FindFirstValue(ClaimTypes.Role) ?? "";

        // Skip admin roles — they manage user bans, shouldn't be blocked mid-action
        var adminRoles = new HashSet<string> { "super_admin", "moderator", "finance_manager", "support_agent", "content_reviewer" };
        if (adminRoles.Contains(role))
        {
            await _next(context);
            return;
        }

        var uidStr = context.User.FindFirstValue("uid");
        if (!Guid.TryParse(uidStr, out var userId))
        {
            await _next(context);
            return;
        }

        // Check status from DB
        string? status = null;
        try
        {
            using var conn = await db.CreateConnectionAsync();
            status = await DbHelper.ExecuteScalarAsync<string?>(conn,
                "SELECT status FROM users WHERE id = @id",
                new Dictionary<string, object?> { ["id"] = userId });
        }
        catch
        {
            // If DB check fails, let the request through (fail-open to avoid downtime)
            await _next(context);
            return;
        }

        if (status == "banned" || status == "suspended" || status == "deleted")
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            var body = JsonSerializer.Serialize(new
            {
                success = false,
                message = status == "banned"
                    ? "Aapka account ban ho gaya hai. Support se contact karein."
                    : "Aapka account suspend hai."
            });
            await context.Response.WriteAsync(body);
            return;
        }

        await _next(context);
    }
}
