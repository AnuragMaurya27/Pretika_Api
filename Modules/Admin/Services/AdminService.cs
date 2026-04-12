using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Infrastructure.Database;
using HauntedVoiceUniverse.Modules.Admin.Models;
using HauntedVoiceUniverse.Modules.Notifications.Services;
using Npgsql;
using System.Text.Json;

namespace HauntedVoiceUniverse.Modules.Admin.Services;

public interface IAdminService
{
    // War Room
    Task<WarRoomResponse> GetWarRoomAsync();

    // Users
    Task<PagedResult<AdminUserResponse>> GetUsersAsync(string? search, string? role, string? status, int page, int pageSize);
    Task<AdminUserResponse?> GetUserDetailAsync(Guid userId);
    Task<(bool Success, string Message)> BanUserAsync(Guid adminId, Guid userId, BanUserRequest req);
    Task<(bool Success, string Message)> UnbanUserAsync(Guid adminId, Guid userId);
    Task<(bool Success, string Message)> FreezeWalletAsync(Guid adminId, Guid userId, string reason);
    Task<(bool Success, string Message)> UnfreezeWalletAsync(Guid adminId, Guid userId);
    Task<(bool Success, string Message)> CreditCoinsAsync(Guid adminId, Guid userId, CreditCoinsRequest req);
    Task<(bool Success, string Message)> AddStrikeAsync(Guid adminId, Guid userId, AddStrikeRequest req);
    Task<(bool Success, string Message)> RemoveStrikeAsync(Guid adminId, Guid strikeId, string reason);

    // Creator Management
    Task<(bool Success, string Message)> ApproveCreatorAsync(Guid adminId, Guid userId, ApproveCreatorRequest req);
    Task<(bool Success, string Message)> ToggleMonetizationAsync(Guid adminId, Guid userId, bool enable);
    Task<(bool Success, string Message)> VerifyCreatorAsync(Guid adminId, Guid userId);
    Task<(bool Success, string Message)> BoostCreatorAsync(Guid adminId, Guid userId, Guid storyId);

    // Stories
    Task<PagedResult<AdminStoryResponse>> GetStoriesAsync(string? search, string? status, int page, int pageSize);
    Task<(bool Success, string Message)> FeatureStoryAsync(Guid adminId, Guid storyId, FeatureStoryRequest req);
    Task<(bool Success, string Message)> RemoveStoryAsync(Guid adminId, Guid storyId, RemoveStoryRequest req);

    // Reports
    Task<PagedResult<AdminReportResponse>> GetReportsAsync(string? status, string? severity, string? entityType, int page, int pageSize);
    Task<(bool Success, string Message)> ResolveReportAsync(Guid adminId, Guid reportId, ResolveReportRequest req);

    // Withdrawals
    Task<PagedResult<AdminWithdrawalResponse>> GetWithdrawalsAsync(string? status, int page, int pageSize);
    Task<(bool Success, string Message)> ApproveWithdrawalAsync(Guid adminId, Guid withdrawalId, ApproveWithdrawalRequest req);
    Task<(bool Success, string Message)> RejectWithdrawalAsync(Guid adminId, Guid withdrawalId, RejectWithdrawalRequest req);

    // Announcements
    Task<(bool Success, string Message)> CreateAnnouncementAsync(Guid adminId, CreateAnnouncementRequest req);
    Task<(bool Success, string Message)> DeleteAnnouncementAsync(Guid adminId, Guid announcementId);

    // Fraud
    Task<PagedResult<FraudAlertResponse>> GetFraudAlertsAsync(bool unresolvedOnly, int page, int pageSize);
    Task<(bool Success, string Message)> ResolveFraudAlertAsync(Guid adminId, Guid alertId, ResolveFraudAlertRequest req);

    // Algorithm Config
    Task<List<AlgorithmConfigResponse>> GetAlgorithmConfigsAsync();
    Task<(bool Success, string Message)> UpdateAlgorithmConfigAsync(Guid adminId, string algorithmName, UpdateAlgorithmConfigRequest req);

    // Emergency Overrides
    Task<List<EmergencyOverrideResponse>> GetEmergencyOverridesAsync();
    Task<(bool Success, string Message)> ToggleOverrideAsync(Guid adminId, string overrideType, ToggleOverrideRequest req);

    // Analytics
    Task<PlatformAnalyticsResponse> GetAnalyticsAsync();

    // Support (Admin view)
    Task<PagedResult<AdminWithdrawalResponse>> GetAllTicketsAsync(string? status, int page, int pageSize);

    // Platform Settings
    Task<List<PlatformSettingResponse>> GetPlatformSettingsAsync();
    Task<(bool Success, string Message)> UpdatePlatformSettingAsync(string key, string value);
}

public class AdminService : IAdminService
{
    private readonly IDbConnectionFactory _db;
    // BUG#M9-3 FIX: inject notification service to alert users about moderation actions.
    private readonly INotificationService _notify;

    public AdminService(IDbConnectionFactory db, INotificationService notify)
    {
        _db = db;
        _notify = notify;
    }

    private async Task LogAuditAsync(NpgsqlConnection conn, Guid adminId, string action,
        string? entityType = null, Guid? entityId = null, string? description = null,
        object? oldValue = null, object? newValue = null)
    {
        try
        {
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO admin_audit_log (admin_id, action, entity_type, entity_id, description, old_value, new_value)
                  VALUES (@aid, @action, @et, @eid, @desc, @old::jsonb, @new::jsonb)",
                new Dictionary<string, object?>
                {
                    ["aid"] = adminId,
                    ["action"] = action,
                    ["et"] = (object?)entityType ?? DBNull.Value,
                    ["eid"] = (object?)entityId ?? DBNull.Value,
                    ["desc"] = (object?)description ?? DBNull.Value,
                    ["old"] = oldValue != null ? JsonSerializer.Serialize(oldValue) : (object)DBNull.Value,
                    ["new"] = newValue != null ? JsonSerializer.Serialize(newValue) : (object)DBNull.Value
                });
        }
        catch { /* Audit log failure should not break main action */ }
    }

    public async Task<WarRoomResponse> GetWarRoomAsync()
    {
        using var conn = await _db.CreateConnectionAsync();
        // BUG#A8 FIX: Use IST (UTC+5:30) midnight — platform is Indian
        // UTC midnight = 5:30 AM IST which is wrong for "today's" stats
        var istOffset = TimeSpan.FromHours(5.5);
        var today = (DateTime.UtcNow + istOffset).Date - istOffset; // IST midnight in UTC

        var activeToday = await DbHelper.ExecuteScalarAsync<long>(conn,
            "SELECT COUNT(*) FROM users WHERE last_active_at >= @today", new Dictionary<string, object?> { ["today"] = today });
        var newToday = await DbHelper.ExecuteScalarAsync<long>(conn,
            "SELECT COUNT(*) FROM users WHERE created_at >= @today", new Dictionary<string, object?> { ["today"] = today });
        var coinsToday = await DbHelper.ExecuteScalarAsync<long>(conn,
            "SELECT COALESCE(SUM(amount),0) FROM coin_transactions WHERE transaction_type = 'recharge' AND status = 'completed' AND created_at >= @today",
            new Dictionary<string, object?> { ["today"] = today });
        var withdrawnToday = await DbHelper.ExecuteScalarAsync<long>(conn,
            "SELECT COALESCE(SUM(coin_amount),0) FROM withdrawals WHERE status = 'completed' AND processed_at >= @today",
            new Dictionary<string, object?> { ["today"] = today });
        var pendingReports = await DbHelper.ExecuteScalarAsync<long>(conn, "SELECT COUNT(*) FROM reports WHERE status = 'pending'");
        var pendingWithdrawals = await DbHelper.ExecuteScalarAsync<long>(conn, "SELECT COUNT(*) FROM withdrawals WHERE status = 'pending'");
        var totalUsers = await DbHelper.ExecuteScalarAsync<long>(conn, "SELECT COUNT(*) FROM users WHERE deleted_at IS NULL");
        var totalCreators = await DbHelper.ExecuteScalarAsync<long>(conn, "SELECT COUNT(*) FROM users WHERE is_creator = TRUE AND deleted_at IS NULL");
        var totalStories = await DbHelper.ExecuteScalarAsync<long>(conn, "SELECT COUNT(*) FROM stories WHERE deleted_at IS NULL");
        var rewardFund = await DbHelper.ExecuteScalarAsync<long>(conn, "SELECT COALESCE(balance,0) FROM reward_fund_pool LIMIT 1");
        var fraudAlerts = await DbHelper.ExecuteScalarAsync<long>(conn, "SELECT COUNT(*) FROM fraud_alerts WHERE is_resolved = FALSE");
        var openTickets = await DbHelper.ExecuteScalarAsync<long>(conn, "SELECT COUNT(*) FROM support_tickets WHERE status IN ('open','in_progress','waiting_user')");

        return new WarRoomResponse
        {
            ActiveUsersToday = activeToday,
            NewUsersToday = newToday,
            RevenueCoinsToday = coinsToday,
            CoinsPurchasedToday = coinsToday,
            CoinsWithdrawnToday = withdrawnToday,
            PendingReports = pendingReports,
            PendingWithdrawals = pendingWithdrawals,
            TotalUsers = totalUsers,
            TotalCreators = totalCreators,
            TotalStories = totalStories,
            RewardFundBalance = rewardFund,
            UnresolvedFraudAlerts = fraudAlerts,
            OpenTickets = openTickets
        };
    }

    public async Task<PagedResult<AdminUserResponse>> GetUsersAsync(string? search, string? role, string? status, int page, int pageSize)
    {
        using var conn = await _db.CreateConnectionAsync();
        pageSize = Math.Min(pageSize, 100);
        int offset = (page - 1) * pageSize;

        var whereParts = new List<string> { "u.deleted_at IS NULL" };
        var parameters = new Dictionary<string, object?>();

        if (!string.IsNullOrEmpty(search))
        {
            whereParts.Add("(u.username ILIKE @search OR u.email ILIKE @search OR u.display_name ILIKE @search)");
            parameters["search"] = $"%{search}%";
        }
        if (!string.IsNullOrEmpty(role)) { whereParts.Add("u.role = @role::user_role"); parameters["role"] = role; }
        if (!string.IsNullOrEmpty(status)) { whereParts.Add("u.status = @status::user_status"); parameters["status"] = status; }

        var where = string.Join(" AND ", whereParts);
        var total = await DbHelper.ExecuteScalarAsync<long>(conn, $"SELECT COUNT(*) FROM users u WHERE {where}", parameters);

        parameters["limit"] = pageSize;
        parameters["offset"] = offset;

        var sql = $@"
            SELECT u.id, u.username, u.email, u.display_name, u.avatar_url,
                   u.role, u.status, u.is_creator, u.is_verified_creator, u.is_monetization_enabled,
                   u.strike_count, u.total_stories_published, u.total_followers, u.total_coins_earned,
                   u.state, u.last_login_at, u.created_at, u.banned_until, u.ban_reason,
                   COALESCE(w.coin_balance, 0) as wallet_balance, COALESCE(w.is_frozen, FALSE) as wallet_frozen
            FROM users u
            LEFT JOIN wallets w ON w.user_id = u.id
            WHERE {where}
            ORDER BY u.created_at DESC
            LIMIT @limit OFFSET @offset";

        var items = await DbHelper.ExecuteReaderAsync(conn, sql, r => new AdminUserResponse
        {
            Id = DbHelper.GetGuid(r, "id"),
            Username = DbHelper.GetString(r, "username"),
            Email = DbHelper.GetString(r, "email"),
            DisplayName = DbHelper.GetStringOrNull(r, "display_name"),
            AvatarUrl = DbHelper.GetStringOrNull(r, "avatar_url"),
            Role = DbHelper.GetString(r, "role"),
            Status = DbHelper.GetString(r, "status"),
            IsCreator = DbHelper.GetBool(r, "is_creator"),
            IsVerifiedCreator = DbHelper.GetBool(r, "is_verified_creator"),
            IsMonetizationEnabled = DbHelper.GetBool(r, "is_monetization_enabled"),
            StrikeCount = DbHelper.GetInt(r, "strike_count"),
            TotalStoriesPublished = DbHelper.GetLong(r, "total_stories_published"),
            TotalFollowers = DbHelper.GetLong(r, "total_followers"),
            TotalCoinsEarned = DbHelper.GetLong(r, "total_coins_earned"),
            State = DbHelper.GetStringOrNull(r, "state"),
            LastLoginAt = DbHelper.GetDateTimeOrNull(r, "last_login_at"),
            CreatedAt = DbHelper.GetDateTime(r, "created_at"),
            BannedUntil = DbHelper.GetDateTimeOrNull(r, "banned_until"),
            BanReason = DbHelper.GetStringOrNull(r, "ban_reason"),
            WalletBalance = DbHelper.GetLong(r, "wallet_balance"),
            WalletFrozen = DbHelper.GetBool(r, "wallet_frozen")
        }, parameters);

        return PagedResult<AdminUserResponse>.Create(items, (int)total, page, pageSize);
    }

    public async Task<AdminUserResponse?> GetUserDetailAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await DbHelper.ExecuteReaderFirstAsync(conn,
            @"SELECT u.id, u.username, u.email, u.display_name, u.avatar_url,
                     u.role, u.status, u.is_creator, u.is_verified_creator, u.is_monetization_enabled,
                     u.strike_count, u.total_stories_published, u.total_followers, u.total_coins_earned,
                     u.state, u.last_login_at, u.created_at, u.banned_until, u.ban_reason,
                     COALESCE(w.coin_balance, 0) as wallet_balance, COALESCE(w.is_frozen, FALSE) as wallet_frozen
              FROM users u
              LEFT JOIN wallets w ON w.user_id = u.id
              WHERE u.id = @id AND u.deleted_at IS NULL",
            r => new AdminUserResponse
            {
                Id = DbHelper.GetGuid(r, "id"),
                Username = DbHelper.GetString(r, "username"),
                Email = DbHelper.GetString(r, "email"),
                DisplayName = DbHelper.GetStringOrNull(r, "display_name"),
                AvatarUrl = DbHelper.GetStringOrNull(r, "avatar_url"),
                Role = DbHelper.GetString(r, "role"),
                Status = DbHelper.GetString(r, "status"),
                IsCreator = DbHelper.GetBool(r, "is_creator"),
                IsVerifiedCreator = DbHelper.GetBool(r, "is_verified_creator"),
                IsMonetizationEnabled = DbHelper.GetBool(r, "is_monetization_enabled"),
                StrikeCount = DbHelper.GetInt(r, "strike_count"),
                TotalStoriesPublished = DbHelper.GetLong(r, "total_stories_published"),
                TotalFollowers = DbHelper.GetLong(r, "total_followers"),
                TotalCoinsEarned = DbHelper.GetLong(r, "total_coins_earned"),
                State = DbHelper.GetStringOrNull(r, "state"),
                LastLoginAt = DbHelper.GetDateTimeOrNull(r, "last_login_at"),
                CreatedAt = DbHelper.GetDateTime(r, "created_at"),
                BannedUntil = DbHelper.GetDateTimeOrNull(r, "banned_until"),
                BanReason = DbHelper.GetStringOrNull(r, "ban_reason"),
                WalletBalance = DbHelper.GetLong(r, "wallet_balance"),
                WalletFrozen = DbHelper.GetBool(r, "wallet_frozen")
            },
            new Dictionary<string, object?> { ["id"] = userId });
    }

    public async Task<(bool Success, string Message)> BanUserAsync(Guid adminId, Guid userId, BanUserRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();
        var status = req.ShadowBan ? "shadow_banned" : "banned";
        DateTime? bannedUntil = req.DurationHours.HasValue ? DateTime.UtcNow.AddHours(req.DurationHours.Value) : null;

        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE users SET status = @status::user_status, ban_reason = @reason, banned_until = @until, updated_at = NOW() WHERE id = @id",
            new Dictionary<string, object?> { ["status"] = status, ["reason"] = req.Reason, ["until"] = (object?)bannedUntil ?? DBNull.Value, ["id"] = userId });

        await DbHelper.ExecuteNonQueryAsync(conn,
            @"INSERT INTO moderation_log (moderator_id, target_user_id, action, reason, ban_duration_hours, ban_expires_at)
              VALUES (@aid, @uid, @action::moderation_action, @reason, @dur, @exp)",
            new Dictionary<string, object?>
            {
                ["aid"] = adminId, ["uid"] = userId,
                ["action"] = req.DurationHours.HasValue ? "temp_ban" : (req.ShadowBan ? "shadow_ban" : "permanent_ban"),
                ["reason"] = req.Reason,
                ["dur"] = (object?)req.DurationHours ?? DBNull.Value,
                ["exp"] = (object?)bannedUntil ?? DBNull.Value
            });

        await LogAuditAsync(conn, adminId, "ban_user", "user", userId, req.Reason);

        // BUG#M9-3 FIX: Notify the user about the ban action.
        var banTitle = req.ShadowBan ? "Account Warning" : (req.DurationHours.HasValue ? "Account Temporarily Suspended" : "Account Banned");
        var banMsg   = req.ShadowBan
            ? $"Aapki account activity review under hai. Reason: {req.Reason}"
            : req.DurationHours.HasValue
                ? $"Aapka account {req.DurationHours}h ke liye suspend ho gaya hai. Reason: {req.Reason}"
                : $"Aapka account permanently ban ho gaya hai. Reason: {req.Reason}. Appeal ke liye support se contact karein.";
        _ = _notify.CreateNotificationAsync(userId, "system", banTitle, banMsg);

        return (true, "User ban ho gaya");
    }

    public async Task<(bool Success, string Message)> UnbanUserAsync(Guid adminId, Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE users SET status = 'active', ban_reason = NULL, banned_until = NULL, updated_at = NOW() WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = userId });
        await LogAuditAsync(conn, adminId, "unban_user", "user", userId);
        return (true, "User unban ho gaya");
    }

    public async Task<(bool Success, string Message)> FreezeWalletAsync(Guid adminId, Guid userId, string reason)
    {
        using var conn = await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE wallets SET is_frozen = TRUE, frozen_reason = @reason, frozen_at = NOW(), frozen_by = @aid WHERE user_id = @uid",
            new Dictionary<string, object?> { ["reason"] = reason, ["aid"] = adminId, ["uid"] = userId });
        await LogAuditAsync(conn, adminId, "freeze_wallet", "user", userId, reason);
        return (true, "Wallet freeze ho gaya");
    }

    public async Task<(bool Success, string Message)> UnfreezeWalletAsync(Guid adminId, Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE wallets SET is_frozen = FALSE, frozen_reason = NULL, frozen_at = NULL, frozen_by = NULL WHERE user_id = @uid",
            new Dictionary<string, object?> { ["uid"] = userId });
        await LogAuditAsync(conn, adminId, "unfreeze_wallet", "user", userId);
        return (true, "Wallet unfreeze ho gaya");
    }

    public async Task<(bool Success, string Message)> CreditCoinsAsync(Guid adminId, Guid userId, CreditCoinsRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();
        using var tx = await conn.BeginTransactionAsync();
        try
        {
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO coin_transactions (sender_id, receiver_id, transaction_type, status, amount, description, completed_at)
                  VALUES (@aid, @uid, @type::transaction_type, 'completed', @amount, @desc, NOW())",
                new Dictionary<string, object?> { ["aid"] = adminId, ["uid"] = userId, ["type"] = req.TransactionType, ["amount"] = req.Amount, ["desc"] = req.Reason }, tx);

            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO wallets (user_id, coin_balance, total_earned) VALUES (@uid, @amount, @amount)
                  ON CONFLICT (user_id) DO UPDATE
                  SET coin_balance = wallets.coin_balance + @amount, total_earned = wallets.total_earned + @amount, updated_at = NOW()",
                new Dictionary<string, object?> { ["uid"] = userId, ["amount"] = req.Amount }, tx);

            await tx.CommitAsync();
            await LogAuditAsync(conn, adminId, "credit_coins", "user", userId, req.Reason, null, new { amount = req.Amount });
            return (true, $"{req.Amount} coins credit ho gaye");
        }
        catch { await tx.RollbackAsync(); return (false, "Coins credit karne mein error"); }
    }

    public async Task<(bool Success, string Message)> AddStrikeAsync(Guid adminId, Guid userId, AddStrikeRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();
        // BUG#A6 FIX: Wrap in transaction with FOR UPDATE to prevent race condition
        // where two concurrent admins both see strikeNum < 3 and both trigger auto-ban
        using var tx = await conn.BeginTransactionAsync();
        try
        {
            var strikeNum = await DbHelper.ExecuteScalarAsync<int>(conn,
                "SELECT strike_count FROM users WHERE id = @id FOR UPDATE",
                new Dictionary<string, object?> { ["id"] = userId });

            if (strikeNum >= 3) { await tx.RollbackAsync(); return (false, "User ke pehle se 3 strikes hain. Ban karo."); }

            strikeNum++;
            await DbHelper.ExecuteNonQueryAsync(conn,
                "INSERT INTO user_strikes (user_id, issued_by, reason, strike_number, report_id) VALUES (@uid, @aid, @reason, @num, @rid)",
                new Dictionary<string, object?> { ["uid"] = userId, ["aid"] = adminId, ["reason"] = req.Reason, ["num"] = strikeNum, ["rid"] = (object?)req.ReportId ?? DBNull.Value }, tx);

            if (strikeNum >= 3)
            {
                await DbHelper.ExecuteNonQueryAsync(conn,
                    "UPDATE users SET strike_count = @num, last_strike_at = NOW(), status = 'banned'::user_status, ban_reason = '3 strikes - auto ban' WHERE id = @id",
                    new Dictionary<string, object?> { ["id"] = userId, ["num"] = strikeNum }, tx);
            }
            else
            {
                await DbHelper.ExecuteNonQueryAsync(conn,
                    "UPDATE users SET strike_count = @num, last_strike_at = NOW() WHERE id = @id",
                    new Dictionary<string, object?> { ["id"] = userId, ["num"] = strikeNum }, tx);
            }

            await tx.CommitAsync();
            await LogAuditAsync(conn, adminId, "add_strike", "user", userId, req.Reason);

            // BUG#M9-3 FIX: Notify user about their strike with the reason.
            var strikeTitle = strikeNum >= 3 ? "⛔ Account Ban — 3 Strikes" : $"⚠️ Warning Strike #{strikeNum}";
            var strikeMsg   = strikeNum >= 3
                ? $"Aapko 3 strikes mil gayi hain aur aapka account ban ho gaya hai. Last reason: {req.Reason}"
                : $"Aapko strike #{strikeNum} mili hai. Reason: {req.Reason}. 3 strikes pe permanent ban hoga.";
            _ = _notify.CreateNotificationAsync(userId, "system", strikeTitle, strikeMsg);

            return (true, $"Strike #{strikeNum} add ho gaya" + (strikeNum >= 3 ? " - User auto-ban ho gaya" : ""));
        }
        catch { await tx.RollbackAsync(); return (false, "Strike add karne mein error aaya"); }
    }

    public async Task<(bool Success, string Message)> RemoveStrikeAsync(Guid adminId, Guid strikeId, string reason)
    {
        using var conn = await _db.CreateConnectionAsync();
        var strike = await DbHelper.ExecuteReaderFirstAsync(conn,
            "SELECT id, user_id FROM user_strikes WHERE id = @id AND is_active = TRUE",
            r => new { Id = DbHelper.GetGuid(r, "id"), UserId = DbHelper.GetGuid(r, "user_id") },
            new Dictionary<string, object?> { ["id"] = strikeId });

        if (strike == null) return (false, "Strike nahi mila");

        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE user_strikes SET is_active = FALSE, removed_at = NOW(), removed_by = @aid, remove_reason = @reason WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = strikeId, ["aid"] = adminId, ["reason"] = reason });

        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE users SET strike_count = (SELECT COUNT(*) FROM user_strikes WHERE user_id = @uid AND is_active = TRUE) WHERE id = @uid",
            new Dictionary<string, object?> { ["uid"] = strike.UserId });

        await LogAuditAsync(conn, adminId, "remove_strike", "user", strike.UserId, reason);
        return (true, "Strike remove ho gaya");
    }

    public async Task<(bool Success, string Message)> ApproveCreatorAsync(Guid adminId, Guid userId, ApproveCreatorRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(conn,
            @"UPDATE users SET is_creator = TRUE, creator_approved_at = NOW(), role = 'creator',
              is_monetization_enabled = @mono, monetization_approved_at = CASE WHEN @mono THEN NOW() ELSE NULL END
              WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = userId, ["mono"] = req.EnableMonetization });
        await LogAuditAsync(conn, adminId, "approve_creator", "user", userId);
        return (true, "Creator approve ho gaya");
    }

    public async Task<(bool Success, string Message)> ToggleMonetizationAsync(Guid adminId, Guid userId, bool enable)
    {
        using var conn = await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE users SET is_monetization_enabled = @enable, monetization_approved_at = CASE WHEN @enable THEN NOW() ELSE monetization_approved_at END WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = userId, ["enable"] = enable });
        await LogAuditAsync(conn, adminId, enable ? "enable_monetization" : "disable_monetization", "user", userId);
        return (true, enable ? "Monetization enable ho gayi" : "Monetization pause ho gayi");
    }

    public async Task<(bool Success, string Message)> VerifyCreatorAsync(Guid adminId, Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE users SET is_verified_creator = TRUE, verified_at = NOW() WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = userId });
        await LogAuditAsync(conn, adminId, "verify_creator", "user", userId);
        return (true, "Creator verified ho gaya");
    }

    public async Task<(bool Success, string Message)> BoostCreatorAsync(Guid adminId, Guid userId, Guid storyId)
    {
        using var conn = await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE stories SET homepage_boost_until = NOW() + INTERVAL '7 days', featured_at = NOW() WHERE id = @id AND creator_id = @uid",
            new Dictionary<string, object?> { ["id"] = storyId, ["uid"] = userId });
        await LogAuditAsync(conn, adminId, "boost_story", "story", storyId);
        return (true, "Story 7 din ke liye homepage pe boost ho gayi");
    }

    public async Task<PagedResult<AdminStoryResponse>> GetStoriesAsync(string? search, string? status, int page, int pageSize)
    {
        using var conn = await _db.CreateConnectionAsync();
        pageSize = Math.Min(pageSize, 100);
        int offset = (page - 1) * pageSize;

        var whereParts = new List<string> { "s.deleted_at IS NULL" };
        var parameters = new Dictionary<string, object?>();

        if (!string.IsNullOrEmpty(search)) { whereParts.Add("s.title ILIKE @search"); parameters["search"] = $"%{search}%"; }
        if (!string.IsNullOrEmpty(status)) { whereParts.Add("s.status = @status::story_status"); parameters["status"] = status; }

        var where = string.Join(" AND ", whereParts);
        var total = await DbHelper.ExecuteScalarAsync<long>(conn, $"SELECT COUNT(*) FROM stories s WHERE {where}", parameters);

        parameters["limit"] = pageSize;
        parameters["offset"] = offset;

        var items = await DbHelper.ExecuteReaderAsync(conn,
            $@"SELECT s.id, s.title, s.slug, s.status, s.thumbnail_url, s.creator_id,
                      s.total_views, s.total_likes, s.total_comments, s.total_coins_earned,
                      s.featured_at, s.is_editor_pick, s.published_at, s.created_at, s.age_rating,
                      u.username as creator_username
               FROM stories s
               JOIN users u ON u.id = s.creator_id
               WHERE {where}
               ORDER BY s.created_at DESC
               LIMIT @limit OFFSET @offset",
            r => new AdminStoryResponse
            {
                Id = DbHelper.GetGuid(r, "id"),
                Title = DbHelper.GetString(r, "title"),
                Slug = DbHelper.GetString(r, "slug"),
                Status = DbHelper.GetString(r, "status"),
                ThumbnailUrl = DbHelper.GetStringOrNull(r, "thumbnail_url"),
                CreatorId = DbHelper.GetGuid(r, "creator_id"),
                CreatorUsername = DbHelper.GetString(r, "creator_username"),
                TotalViews = DbHelper.GetLong(r, "total_views"),
                TotalLikes = DbHelper.GetLong(r, "total_likes"),
                TotalComments = DbHelper.GetLong(r, "total_comments"),
                TotalCoinsEarned = DbHelper.GetLong(r, "total_coins_earned"),
                IsFeatured = !r.IsDBNull(r.GetOrdinal("featured_at")),
                IsEditorPick = DbHelper.GetBool(r, "is_editor_pick"),
                PublishedAt = DbHelper.GetDateTimeOrNull(r, "published_at"),
                CreatedAt = DbHelper.GetDateTime(r, "created_at"),
                AgeRating = DbHelper.GetStringOrNull(r, "age_rating")
            }, parameters);

        return PagedResult<AdminStoryResponse>.Create(items, (int)total, page, pageSize);
    }

    public async Task<(bool Success, string Message)> FeatureStoryAsync(Guid adminId, Guid storyId, FeatureStoryRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();
        var boostUntil = req.HomepageBoostDays.HasValue ? (object)DateTime.UtcNow.AddDays(req.HomepageBoostDays.Value) : DBNull.Value;

        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE stories SET featured_at = NOW(), is_editor_pick = @pick, homepage_boost_until = @boost WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = storyId, ["pick"] = req.IsEditorPick, ["boost"] = boostUntil });
        await LogAuditAsync(conn, adminId, "feature_story", "story", storyId);
        return (true, "Story featured ho gayi");
    }

    public async Task<(bool Success, string Message)> RemoveStoryAsync(Guid adminId, Guid storyId, RemoveStoryRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE stories SET status = 'removed', removed_reason = @reason, updated_at = NOW() WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = storyId, ["reason"] = req.Reason });
        await LogAuditAsync(conn, adminId, "remove_story", "story", storyId, req.Reason);
        return (true, "Story remove ho gayi");
    }

    public async Task<PagedResult<AdminReportResponse>> GetReportsAsync(string? status, string? severity, string? entityType, int page, int pageSize)
    {
        using var conn = await _db.CreateConnectionAsync();
        pageSize = Math.Min(pageSize, 100);
        int offset = (page - 1) * pageSize;

        var whereParts = new List<string> { "1=1" };
        var parameters = new Dictionary<string, object?>();

        if (!string.IsNullOrEmpty(status)) { whereParts.Add("r.status = @status::report_status"); parameters["status"] = status; }
        else { whereParts.Add("r.status = 'pending'"); }
        if (!string.IsNullOrEmpty(severity)) { whereParts.Add("r.severity = @severity::report_severity"); parameters["severity"] = severity; }
        if (!string.IsNullOrEmpty(entityType)) { whereParts.Add("r.entity_type = @etype::report_entity_type"); parameters["etype"] = entityType; }

        var where = string.Join(" AND ", whereParts);
        var total = await DbHelper.ExecuteScalarAsync<long>(conn, $"SELECT COUNT(*) FROM reports r WHERE {where}", parameters);

        parameters["limit"] = pageSize;
        parameters["offset"] = offset;

        var items = await DbHelper.ExecuteReaderAsync(conn,
            // BUG#M9-8 FIX: Sort by severity DESC first, then by report count (unique reporters on
            // the same entity) DESC so content with many reports surfaces before single-report items,
            // then oldest-first so nothing is starved.
            $@"SELECT r.id, r.reporter_id, r.entity_type, r.entity_id, r.reason, r.custom_reason,
                      r.severity, r.status, r.created_at, r.reviewed_at, r.action_taken, r.resolution_note,
                      u.username as reporter_username, rv.username as reviewed_by_username,
                      (SELECT COUNT(DISTINCT r2.reporter_id) FROM reports r2
                       WHERE r2.entity_id = r.entity_id AND r2.entity_type = r.entity_type
                         AND r2.status = 'pending') AS unique_reporter_count
               FROM reports r
               JOIN users u ON u.id = r.reporter_id
               LEFT JOIN users rv ON rv.id = r.reviewed_by
               WHERE {where}
               ORDER BY r.severity DESC, unique_reporter_count DESC, r.created_at ASC
               LIMIT @limit OFFSET @offset",
            r => new AdminReportResponse
            {
                Id = DbHelper.GetGuid(r, "id"),
                ReporterId = DbHelper.GetGuid(r, "reporter_id"),
                ReporterUsername = DbHelper.GetString(r, "reporter_username"),
                EntityType = DbHelper.GetString(r, "entity_type"),
                EntityId = DbHelper.GetGuid(r, "entity_id"),
                Reason = DbHelper.GetString(r, "reason"),
                CustomReason = DbHelper.GetStringOrNull(r, "custom_reason"),
                Severity = DbHelper.GetString(r, "severity"),
                Status = DbHelper.GetString(r, "status"),
                CreatedAt = DbHelper.GetDateTime(r, "created_at"),
                ReviewedAt = DbHelper.GetDateTimeOrNull(r, "reviewed_at"),
                ReviewedByUsername = DbHelper.GetStringOrNull(r, "reviewed_by_username"),
                ActionTaken = DbHelper.GetStringOrNull(r, "action_taken"),
                ResolutionNote = DbHelper.GetStringOrNull(r, "resolution_note")
            }, parameters);

        return PagedResult<AdminReportResponse>.Create(items, (int)total, page, pageSize);
    }

    public async Task<(bool Success, string Message)> ResolveReportAsync(Guid adminId, Guid reportId, ResolveReportRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();
        // BUG#M9-9 FIX: Use a transaction + FOR UPDATE to prevent two moderators simultaneously
        // resolving the same report, which would overwrite each other's action_taken/reviewed_by.
        using var tx = await conn.BeginTransactionAsync();

        var report = await DbHelper.ExecuteReaderFirstAsync(conn,
            "SELECT id, entity_id, entity_type, reporter_id FROM reports WHERE id = @id AND status = 'pending' FOR UPDATE",
            r => new { Id = DbHelper.GetGuid(r, "id"), EntityId = DbHelper.GetGuid(r, "entity_id"), EntityType = DbHelper.GetString(r, "entity_type"), ReporterId = DbHelper.GetGuid(r, "reporter_id") },
            new Dictionary<string, object?> { ["id"] = reportId }, tx);

        if (report == null) { await tx.RollbackAsync(); return (false, "Report nahi mila ya pehle se resolved hai"); }

        await DbHelper.ExecuteNonQueryAsync(conn,
            @"UPDATE reports SET status = @status::report_status, reviewed_by = @aid, reviewed_at = NOW(),
              action_taken = @action::moderation_action, resolution_note = @note WHERE id = @id",
            new Dictionary<string, object?>
            {
                ["status"] = req.Action,
                ["aid"] = adminId,
                ["action"] = (object?)req.ModerationAction ?? DBNull.Value,
                ["note"] = (object?)req.ResolutionNote ?? DBNull.Value,
                ["id"] = reportId
            }, tx);

        // Apply moderation action if specified
        if (!string.IsNullOrEmpty(req.ModerationAction))
        {
            var logId = Guid.NewGuid();
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO moderation_log (id, moderator_id, action, entity_type, entity_id, reason, report_id)
                  VALUES (@id, @aid, @action::moderation_action, @et, @eid, @reason, @rid)",
                new Dictionary<string, object?>
                {
                    ["id"] = logId, ["aid"] = adminId,
                    ["action"] = req.ModerationAction, ["et"] = report.EntityType,
                    ["eid"] = report.EntityId, ["reason"] = req.ResolutionNote ?? req.ModerationAction,
                    ["rid"] = reportId
                }, tx);
        }

        await tx.CommitAsync(); // BUG#M9-9 FIX: commit the locked transaction
        await LogAuditAsync(conn, adminId, "resolve_report", "report", reportId, req.ResolutionNote);

        // BUG#M9-3 FIX: Notify the owner of the reported content about the moderation outcome
        // (TC27). We notify the entity owner — for story/episode/comment we look up the owner_id.
        // For 'user' type, entity_id IS the userId. Only notify when an actual moderation action was taken.
        if (!string.IsNullOrEmpty(req.ModerationAction) && req.ModerationAction != "no_action")
        {
            Guid notifyTargetId = default;
            switch (report.EntityType)
            {
                case "user":
                    notifyTargetId = report.EntityId;
                    break;
                case "story":
                case "episode":
                    try {
                        using var nc = await _db.CreateConnectionAsync();
                        var cid = await DbHelper.ExecuteScalarAsync<Guid?>(nc,
                            "SELECT creator_id FROM stories WHERE id=@id",
                            new() { ["@id"] = report.EntityId });
                        if (cid.HasValue) notifyTargetId = cid.Value;
                    } catch { }
                    break;
                case "comment":
                    try {
                        using var nc = await _db.CreateConnectionAsync();
                        var uid = await DbHelper.ExecuteScalarAsync<Guid?>(nc,
                            "SELECT user_id FROM comments WHERE id=@id",
                            new() { ["@id"] = report.EntityId });
                        if (uid.HasValue) notifyTargetId = uid.Value;
                    } catch { }
                    break;
            }
            if (notifyTargetId != default)
            {
                var actionNote = req.ResolutionNote ?? req.ModerationAction;
                _ = _notify.CreateNotificationAsync(notifyTargetId, "system",
                    "Moderation Action Taken",
                    $"Aapke content pe ek report ke baad moderation action liya gaya: {req.ModerationAction}. Note: {actionNote}");
            }
        }

        return (true, "Report resolve ho gaya");
    }

    public async Task<PagedResult<AdminWithdrawalResponse>> GetWithdrawalsAsync(string? status, int page, int pageSize)
    {
        using var conn = await _db.CreateConnectionAsync();
        pageSize = Math.Min(pageSize, 100);
        int offset = (page - 1) * pageSize;

        var where = string.IsNullOrEmpty(status) ? "w.status = 'pending'" : $"w.status = @status::withdrawal_status";
        var parameters = new Dictionary<string, object?>();
        if (!string.IsNullOrEmpty(status)) parameters["status"] = status;

        var total = await DbHelper.ExecuteScalarAsync<long>(conn, $"SELECT COUNT(*) FROM withdrawals w WHERE {where}", parameters);

        parameters["limit"] = pageSize;
        parameters["offset"] = offset;

        var items = await DbHelper.ExecuteReaderAsync(conn,
            $@"SELECT w.*, u.username, u.email, a.username as processed_by_username
               FROM withdrawals w
               JOIN users u ON u.id = w.user_id
               LEFT JOIN users a ON a.id = w.processed_by
               WHERE {where}
               ORDER BY w.created_at ASC
               LIMIT @limit OFFSET @offset",
            r => new AdminWithdrawalResponse
            {
                Id = DbHelper.GetGuid(r, "id"),
                UserId = DbHelper.GetGuid(r, "user_id"),
                Username = DbHelper.GetString(r, "username"),
                Email = DbHelper.GetString(r, "email"),
                CoinAmount = DbHelper.GetLong(r, "coin_amount"),
                InrAmount = DbHelper.GetDecimal(r, "inr_amount"),
                TdsAmount = DbHelper.GetDecimal(r, "tds_amount"),
                NetAmount = DbHelper.GetDecimal(r, "net_amount"),
                Status = DbHelper.GetString(r, "status"),
                PaymentMethod = DbHelper.GetString(r, "payment_method"),
                UpiId = DbHelper.GetStringOrNull(r, "upi_id"),
                BankName = DbHelper.GetStringOrNull(r, "bank_name"),
                BankAccountNumber = DbHelper.GetStringOrNull(r, "bank_account_number"),
                BankIfsc = DbHelper.GetStringOrNull(r, "bank_ifsc"),
                AccountHolderName = DbHelper.GetStringOrNull(r, "account_holder_name"),
                CreatedAt = DbHelper.GetDateTime(r, "created_at"),
                ProcessedAt = DbHelper.GetDateTimeOrNull(r, "processed_at"),
                ProcessedByUsername = DbHelper.GetStringOrNull(r, "processed_by_username"),
                TransactionReference = DbHelper.GetStringOrNull(r, "transaction_reference"),
                RejectionReason = DbHelper.GetStringOrNull(r, "rejection_reason")
            }, parameters);

        return PagedResult<AdminWithdrawalResponse>.Create(items, (int)total, page, pageSize);
    }

    public async Task<(bool Success, string Message)> ApproveWithdrawalAsync(Guid adminId, Guid withdrawalId, ApproveWithdrawalRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();
        using var tx = await conn.BeginTransactionAsync();
        try
        {
            var wd = await DbHelper.ExecuteReaderFirstAsync(conn,
                @"SELECT w.id, w.user_id, w.coin_amount, w.status, u.status AS user_status
                  FROM withdrawals w
                  JOIN users u ON u.id = w.user_id
                  WHERE w.id = @id AND w.status = 'pending' FOR UPDATE",
                r => new
                {
                    Id         = DbHelper.GetGuid(r, "id"),
                    UserId     = DbHelper.GetGuid(r, "user_id"),
                    CoinAmount = DbHelper.GetLong(r, "coin_amount"),
                    UserStatus = DbHelper.GetString(r, "user_status"),
                },
                new Dictionary<string, object?> { ["id"] = withdrawalId });

            if (wd == null) return (false, "Withdrawal nahi mila ya pending nahi hai");

            // BUG#9 FIX: Block payout if user is banned — admin must manually reject
            if (wd.UserStatus != "active")
                return (false, $"User ka account '{wd.UserStatus}' hai. Withdrawal approve nahi ho sakta. Pehle reject karein.");

            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE withdrawals SET status = 'completed', processed_by = @aid, processed_at = NOW(), transaction_reference = @ref WHERE id = @id",
                new Dictionary<string, object?> { ["id"] = withdrawalId, ["aid"] = adminId, ["ref"] = req.TransactionReference }, tx);

            // Release held coins
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE wallets SET pending_withdrawal = GREATEST(pending_withdrawal - @amount, 0), total_withdrawn = total_withdrawn + @amount, updated_at = NOW() WHERE user_id = @uid",
                new Dictionary<string, object?> { ["uid"] = wd.UserId, ["amount"] = wd.CoinAmount }, tx);

            // BUG#14 FIX: Audit log INSIDE transaction — atomically committed with the approval
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO admin_audit_log (admin_id, action, entity_type, entity_id, description)
                  VALUES (@aid, 'approve_withdrawal', 'withdrawal', @eid, 'Withdrawal approved')",
                new Dictionary<string, object?> { ["aid"] = adminId, ["eid"] = withdrawalId }, tx);

            await tx.CommitAsync();
            return (true, "Withdrawal approve ho gayi");
        }
        catch { await tx.RollbackAsync(); return (false, "Error aaya"); }
    }

    public async Task<(bool Success, string Message)> RejectWithdrawalAsync(Guid adminId, Guid withdrawalId, RejectWithdrawalRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();
        using var tx = await conn.BeginTransactionAsync();
        try
        {
            var wd = await DbHelper.ExecuteReaderFirstAsync(conn,
                "SELECT id, user_id, coin_amount, status FROM withdrawals WHERE id = @id AND status = 'pending' FOR UPDATE",
                r => new { Id = DbHelper.GetGuid(r, "id"), UserId = DbHelper.GetGuid(r, "user_id"), CoinAmount = DbHelper.GetLong(r, "coin_amount") },
                new Dictionary<string, object?> { ["id"] = withdrawalId });

            if (wd == null) return (false, "Withdrawal nahi mila ya pending nahi hai");

            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE withdrawals SET status = 'rejected', processed_by = @aid, processed_at = NOW(), rejection_reason = @reason WHERE id = @id",
                new Dictionary<string, object?> { ["id"] = withdrawalId, ["aid"] = adminId, ["reason"] = req.Reason }, tx);

            // Refund coins to wallet
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE wallets SET coin_balance = coin_balance + @amount, pending_withdrawal = GREATEST(pending_withdrawal - @amount, 0), updated_at = NOW() WHERE user_id = @uid",
                new Dictionary<string, object?> { ["uid"] = wd.UserId, ["amount"] = wd.CoinAmount }, tx);

            // BUG#A7 FIX: Audit log INSIDE transaction — atomically committed with the rejection
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO admin_audit_log (admin_id, action, entity_type, entity_id, description)
                  VALUES (@aid, 'reject_withdrawal', 'withdrawal', @eid, @reason)",
                new Dictionary<string, object?> { ["aid"] = adminId, ["eid"] = withdrawalId, ["reason"] = req.Reason }, tx);

            await tx.CommitAsync();
            return (true, "Withdrawal reject ho gayi, coins refund ho gaye");
        }
        catch { await tx.RollbackAsync(); return (false, "Error aaya"); }
    }

    public async Task<(bool Success, string Message)> CreateAnnouncementAsync(Guid adminId, CreateAnnouncementRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(conn,
            @"INSERT INTO announcements (title, message, banner_url, display_type, priority, starts_at, ends_at, created_by)
              VALUES (@title, @msg, @banner, @dtype, @priority, @start, @end, @aid)",
            new Dictionary<string, object?>
            {
                ["title"] = req.Title, ["msg"] = req.Message,
                ["banner"] = (object?)req.BannerUrl ?? DBNull.Value,
                ["dtype"] = req.DisplayType, ["priority"] = req.Priority,
                ["start"] = req.StartsAt, ["end"] = req.EndsAt, ["aid"] = adminId
            });
        await LogAuditAsync(conn, adminId, "create_announcement", "announcement", null, req.Title);
        return (true, "Announcement create ho gayi");
    }

    public async Task<(bool Success, string Message)> DeleteAnnouncementAsync(Guid adminId, Guid announcementId)
    {
        using var conn = await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE announcements SET is_active = FALSE WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = announcementId });
        await LogAuditAsync(conn, adminId, "delete_announcement", "announcement", announcementId);
        return (true, "Announcement remove ho gayi");
    }

    public async Task<PagedResult<FraudAlertResponse>> GetFraudAlertsAsync(bool unresolvedOnly, int page, int pageSize)
    {
        using var conn = await _db.CreateConnectionAsync();
        pageSize = Math.Min(pageSize, 100);
        int offset = (page - 1) * pageSize;

        var where = unresolvedOnly ? "fa.is_resolved = FALSE" : "1=1";
        var total = await DbHelper.ExecuteScalarAsync<long>(conn, $"SELECT COUNT(*) FROM fraud_alerts fa WHERE {where}");

        var items = await DbHelper.ExecuteReaderAsync(conn,
            $@"SELECT fa.id, fa.user_id, fa.alert_type, fa.severity, fa.is_resolved,
                      fa.created_at, fa.resolution_action, fa.resolution_note,
                      u.username
               FROM fraud_alerts fa JOIN users u ON u.id = fa.user_id
               WHERE {where}
               ORDER BY fa.created_at DESC
               LIMIT @limit OFFSET @offset",
            r => new FraudAlertResponse
            {
                Id = DbHelper.GetGuid(r, "id"),
                UserId = DbHelper.GetGuid(r, "user_id"),
                Username = DbHelper.GetString(r, "username"),
                AlertType = DbHelper.GetString(r, "alert_type"),
                Severity = DbHelper.GetString(r, "severity"),
                IsResolved = DbHelper.GetBool(r, "is_resolved"),
                CreatedAt = DbHelper.GetDateTime(r, "created_at"),
                ResolutionAction = DbHelper.GetStringOrNull(r, "resolution_action"),
                ResolutionNote = DbHelper.GetStringOrNull(r, "resolution_note")
            },
            new Dictionary<string, object?> { ["limit"] = pageSize, ["offset"] = offset });

        return PagedResult<FraudAlertResponse>.Create(items, (int)total, page, pageSize);
    }

    public async Task<(bool Success, string Message)> ResolveFraudAlertAsync(Guid adminId, Guid alertId, ResolveFraudAlertRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE fraud_alerts SET is_resolved = TRUE, resolved_by = @aid, resolved_at = NOW(), resolution_action = @action, resolution_note = @note WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = alertId, ["aid"] = adminId, ["action"] = req.Action, ["note"] = (object?)req.Note ?? DBNull.Value });
        await LogAuditAsync(conn, adminId, "resolve_fraud_alert", "fraud_alert", alertId);
        return (true, "Fraud alert resolve ho gaya");
    }

    public async Task<List<AlgorithmConfigResponse>> GetAlgorithmConfigsAsync()
    {
        using var conn = await _db.CreateConnectionAsync();
        return await DbHelper.ExecuteReaderAsync(conn,
            "SELECT id, algorithm_name, weights::text as weights_text, description, is_active, updated_at FROM algorithm_config ORDER BY algorithm_name",
            r => new AlgorithmConfigResponse
            {
                Id = DbHelper.GetGuid(r, "id"),
                AlgorithmName = DbHelper.GetString(r, "algorithm_name"),
                Weights = DbHelper.GetString(r, "weights_text"),
                Description = DbHelper.GetStringOrNull(r, "description"),
                IsActive = DbHelper.GetBool(r, "is_active"),
                UpdatedAt = DbHelper.GetDateTime(r, "updated_at")
            });
    }

    public async Task<(bool Success, string Message)> UpdateAlgorithmConfigAsync(Guid adminId, string algorithmName, UpdateAlgorithmConfigRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();
        var rows = await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE algorithm_config SET weights = @weights::jsonb, updated_by = @aid, updated_at = NOW() WHERE algorithm_name = @name",
            new Dictionary<string, object?> { ["weights"] = req.Weights, ["aid"] = adminId, ["name"] = algorithmName });

        if (rows == 0) return (false, "Algorithm config nahi mila");
        await LogAuditAsync(conn, adminId, "update_algorithm", "algorithm_config", null, algorithmName);
        return (true, "Algorithm config update ho gaya");
    }

    public async Task<List<EmergencyOverrideResponse>> GetEmergencyOverridesAsync()
    {
        using var conn = await _db.CreateConnectionAsync();
        return await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT eo.override_type, eo.is_active, eo.reason, eo.activated_at, u.username as activated_by_username
              FROM emergency_overrides eo
              LEFT JOIN users u ON u.id = eo.activated_by",
            r => new EmergencyOverrideResponse
            {
                OverrideType = DbHelper.GetString(r, "override_type"),
                IsActive = DbHelper.GetBool(r, "is_active"),
                Reason = DbHelper.GetStringOrNull(r, "reason"),
                ActivatedAt = DbHelper.GetDateTimeOrNull(r, "activated_at"),
                ActivatedBy = DbHelper.GetStringOrNull(r, "activated_by_username")
            });
    }

    public async Task<(bool Success, string Message)> ToggleOverrideAsync(Guid adminId, string overrideType, ToggleOverrideRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();

        if (req.Activate)
        {
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE emergency_overrides SET is_active = TRUE, activated_by = @aid, activated_at = NOW(), reason = @reason, updated_at = NOW() WHERE override_type = @type",
                new Dictionary<string, object?> { ["aid"] = adminId, ["reason"] = (object?)req.Reason ?? DBNull.Value, ["type"] = overrideType });
        }
        else
        {
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE emergency_overrides SET is_active = FALSE, deactivated_by = @aid, deactivated_at = NOW(), updated_at = NOW() WHERE override_type = @type",
                new Dictionary<string, object?> { ["aid"] = adminId, ["type"] = overrideType });
        }

        await LogAuditAsync(conn, adminId, req.Activate ? $"activate_{overrideType}" : $"deactivate_{overrideType}", "emergency_override", null, req.Reason);
        return (true, req.Activate ? $"{overrideType} activate ho gaya" : $"{overrideType} deactivate ho gaya");
    }

    public async Task<PlatformAnalyticsResponse> GetAnalyticsAsync()
    {
        using var conn = await _db.CreateConnectionAsync();
        // BUG#A8 FIX: Use IST (UTC+5:30) midnight for DAU/MAU calculations
        var istOffset = TimeSpan.FromHours(5.5);
        var todayIst = (DateTime.UtcNow + istOffset).Date;
        var today = todayIst - istOffset; // IST midnight in UTC
        var monthStart = new DateTime(todayIst.Year, todayIst.Month, 1) - istOffset;

        var totalUsers = await DbHelper.ExecuteScalarAsync<long>(conn, "SELECT COUNT(*) FROM users WHERE deleted_at IS NULL");
        var totalCreators = await DbHelper.ExecuteScalarAsync<long>(conn, "SELECT COUNT(*) FROM users WHERE is_creator = TRUE");
        var totalStories = await DbHelper.ExecuteScalarAsync<long>(conn, "SELECT COUNT(*) FROM stories WHERE deleted_at IS NULL");
        var totalEpisodes = await DbHelper.ExecuteScalarAsync<long>(conn, "SELECT COUNT(*) FROM episodes WHERE deleted_at IS NULL");
        var totalViews = await DbHelper.ExecuteScalarAsync<long>(conn, "SELECT COALESCE(SUM(total_views),0) FROM stories");
        var totalComments = await DbHelper.ExecuteScalarAsync<long>(conn, "SELECT COUNT(*) FROM comments WHERE deleted_at IS NULL");
        var totalCoins = await DbHelper.ExecuteScalarAsync<long>(conn, "SELECT COALESCE(SUM(coin_balance),0) FROM wallets");
        var dau = await DbHelper.ExecuteScalarAsync<long>(conn, "SELECT COUNT(*) FROM users WHERE last_active_at >= @today", new Dictionary<string, object?> { ["today"] = today });
        var mau = await DbHelper.ExecuteScalarAsync<long>(conn, "SELECT COUNT(*) FROM users WHERE last_active_at >= @start", new Dictionary<string, object?> { ["start"] = monthStart });

        // Last 30 days DAU
        var last30DaysDau = await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT DATE(created_at) as metric_date, COUNT(DISTINCT user_id) as value
              FROM login_history
              WHERE created_at >= NOW() - INTERVAL '30 days' AND is_successful = TRUE
              GROUP BY DATE(created_at)
              ORDER BY metric_date",
            r => new DailyMetric { Date = DbHelper.GetDateTime(r, "metric_date"), Value = DbHelper.GetLong(r, "value") });

        // Last 30 days Revenue
        var last30DaysRevenue = await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT DATE(created_at) as metric_date, COALESCE(SUM(amount),0) as value
              FROM coin_transactions
              WHERE transaction_type = 'recharge' AND status = 'completed' AND created_at >= NOW() - INTERVAL '30 days'
              GROUP BY DATE(created_at)
              ORDER BY metric_date",
            r => new DailyMetric { Date = DbHelper.GetDateTime(r, "metric_date"), Value = DbHelper.GetLong(r, "value") });

        return new PlatformAnalyticsResponse
        {
            TotalUsers = totalUsers,
            TotalCreators = totalCreators,
            TotalStories = totalStories,
            TotalEpisodes = totalEpisodes,
            TotalViews = totalViews,
            TotalComments = totalComments,
            TotalCoinsCirculation = totalCoins,
            DauToday = dau,
            MauThisMonth = mau,
            Last30DaysDau = last30DaysDau,
            Last30DaysRevenue = last30DaysRevenue
        };
    }

    public async Task<PagedResult<AdminWithdrawalResponse>> GetAllTicketsAsync(string? status, int page, int pageSize)
    {
        // Placeholder - tickets are managed in Support module
        return PagedResult<AdminWithdrawalResponse>.Create(new(), 0, page, pageSize);
    }

    // ─── PLATFORM SETTINGS ────────────────────────────────────────────────────

    public async Task<List<PlatformSettingResponse>> GetPlatformSettingsAsync()
    {
        using var conn = await _db.CreateConnectionAsync();

        // Auto-create table if not exists
        await DbHelper.ExecuteNonQueryAsync(conn,
            @"CREATE TABLE IF NOT EXISTS platform_settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL DEFAULT '',
                description TEXT,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )");

        // Ensure default settings exist
        var defaults = new Dictionary<string, (string Value, string Desc)>
        {
            ["referral_coins"]        = ("100",  "Coins awarded to referrer when someone uses their code"),
            ["signup_bonus_coins"]    = ("50",   "Coins given to new users on signup"),
            ["creator_welcome_coins"] = ("100",  "Coins given when user becomes creator"),
        };

        foreach (var kv in defaults)
        {
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO platform_settings (key, value, description, updated_at)
                  VALUES (@key, @val, @desc, NOW())
                  ON CONFLICT (key) DO NOTHING",
                new Dictionary<string, object?> { ["@key"] = kv.Key, ["@val"] = kv.Value.Value, ["@desc"] = kv.Value.Desc });
        }

        return await DbHelper.ExecuteReaderAsync(conn,
            "SELECT key, value, description, updated_at FROM platform_settings ORDER BY key",
            r => new PlatformSettingResponse
            {
                Key         = DbHelper.GetString(r, "key"),
                Value       = DbHelper.GetString(r, "value"),
                Description = DbHelper.GetStringOrNull(r, "description"),
                UpdatedAt   = DbHelper.GetDateTime(r, "updated_at")
            });
    }

    // BUG#A9 FIX: Allowlist of known numeric keys + positive integer validation
    private static readonly HashSet<string> _allowedSettingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "referral_coins", "signup_bonus_coins", "creator_welcome_coins",
        "min_withdrawal_coins", "max_withdrawal_coins", "coin_to_inr_rate",
        "daily_appreciation_limit", "super_chat_min_coins", "super_chat_max_coins",
        "leaderboard_reward_pool", "daily_signup_limit",
    };

    public async Task<(bool Success, string Message)> UpdatePlatformSettingAsync(string key, string value)
    {
        // Only allow updating keys from the known allowlist
        if (!_allowedSettingKeys.Contains(key))
            return (false, $"Unknown setting key '{key}'. Allowed: {string.Join(", ", _allowedSettingKeys)}");

        // All current settings are positive integers — enforce that
        if (!long.TryParse(value, out var numVal) || numVal < 0)
            return (false, $"Invalid value '{value}'. Setting must be a non-negative integer.");

        using var conn = await _db.CreateConnectionAsync();
        var rows = await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE platform_settings SET value = @val, updated_at = NOW() WHERE key = @key",
            new Dictionary<string, object?> { ["@key"] = key, ["@val"] = value });

        if (rows == 0)
        {
            await DbHelper.ExecuteNonQueryAsync(conn,
                "INSERT INTO platform_settings (key, value, updated_at) VALUES (@key, @val, NOW()) ON CONFLICT (key) DO UPDATE SET value = @val, updated_at = NOW()",
                new Dictionary<string, object?> { ["@key"] = key, ["@val"] = value });
        }

        return (true, $"Setting '{key}' updated to '{value}'.");
    }
}
