using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Infrastructure.Database;
using HauntedVoiceUniverse.Modules.Users.Models;
using Npgsql;

namespace HauntedVoiceUniverse.Modules.Users.Services;

public interface IUserService
{
    Task<UserProfileResponse?> GetPublicProfileAsync(string username, Guid? viewerId);
    Task<MyProfileResponse?> GetMyProfileAsync(Guid userId);
    Task<(bool Success, string Message)> UpdateProfileAsync(Guid userId, UpdateProfileRequest req);
    Task<(bool Success, string Message, string? AvatarUrl)> UpdateAvatarAsync(Guid userId, IFormFile file);
    Task<(bool Success, string Message, string? CoverUrl)> UpdateCoverImageAsync(Guid userId, IFormFile file);
    Task<(bool Success, string Message)> DeleteAccountAsync(Guid userId);
    Task<(bool Success, string Message)> FollowUserAsync(Guid followerId, Guid targetId);
    Task<(bool Success, string Message)> UnfollowUserAsync(Guid followerId, Guid targetId);
    Task<PagedResult<FollowUserResponse>> GetFollowersAsync(Guid userId, Guid? viewerId, PaginationParams pagination);
    Task<PagedResult<FollowUserResponse>> GetFollowingAsync(Guid userId, Guid? viewerId, PaginationParams pagination);
    Task<(bool Success, string Message)> BlockUserAsync(Guid blockerId, Guid targetId);
    Task<(bool Success, string Message)> UnblockUserAsync(Guid blockerId, Guid targetId);
    Task<PagedResult<FollowUserResponse>> GetBlockedUsersAsync(Guid userId, PaginationParams pagination);
    Task<bool> IsBlockedAsync(Guid userId, Guid targetId);
    Task<List<FollowUserResponse>> SearchUsersAsync(string q, int page, int pageSize);
}

public class UserService : IUserService
{
    private readonly IDbConnectionFactory _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<UserService> _logger;

    public UserService(IDbConnectionFactory db, IWebHostEnvironment env, ILogger<UserService> logger)
    {
        _db = db;
        _env = env;
        _logger = logger;
    }

    // ─── GET PUBLIC PROFILE ───────────────────────────────────────────────────
    public async Task<UserProfileResponse?> GetPublicProfileAsync(string username, Guid? viewerId)
    {
        UserProfileResponse? profile = null;

        await using (var conn = await _db.CreateConnectionAsync())
        {
            using var cmd = new NpgsqlCommand(@"
                SELECT id, username, display_name, bio, avatar_url, cover_image_url,
                       role, is_creator, is_verified_creator, is_email_verified,
                       reader_fear_rank, creator_fear_rank, creator_rank_score, reader_rank_score,
                       state, city, preferred_language, total_followers, total_following,
                       total_stories_published, total_views_received, total_coins_earned,
                       login_streak, referral_code, created_at, last_active_at, status
                FROM users
                WHERE LOWER(username) = LOWER(@username)
                  AND deleted_at IS NULL", conn);
            cmd.Parameters.AddWithValue("@username", username);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                // Shadow banned user - sirf khud dekh sakta hai
                var status = DbHelper.GetString(reader, "status");
                if (status == "shadow_banned" && viewerId == null)
                    return null;

                profile = MapToPublicProfile(reader);
            }
        }

        if (profile == null) return null;

        // Viewer context check
        if (viewerId.HasValue && viewerId.Value != profile.Id)
        {
            await using var conn2 = await _db.CreateConnectionAsync();

            var isFollowing = await DbHelper.ExecuteScalarAsync<int>(conn2,
                "SELECT COUNT(1) FROM follows WHERE follower_id=@fid AND following_id=@tid",
                new() { ["@fid"] = viewerId, ["@tid"] = profile.Id });

            var isBlocked = await DbHelper.ExecuteScalarAsync<int>(conn2,
                "SELECT COUNT(1) FROM blocks WHERE blocker_id=@bid AND blocked_id=@tid",
                new() { ["@bid"] = viewerId, ["@tid"] = profile.Id });

            profile.IsFollowing = isFollowing > 0;
            profile.IsBlockedByMe = isBlocked > 0;
        }

        return profile;
    }

    // ─── GET MY PROFILE ───────────────────────────────────────────────────────
    public async Task<MyProfileResponse?> GetMyProfileAsync(Guid userId)
    {
        MyProfileResponse? profile = null;

        await using (var conn = await _db.CreateConnectionAsync())
        {
            using var cmd = new NpgsqlCommand(@"
                SELECT u.id, u.username, u.email, u.display_name, u.bio, u.avatar_url,
                       u.cover_image_url, u.phone, u.gender, u.date_of_birth,
                       u.role, u.is_creator, u.is_verified_creator, u.is_email_verified,
                       u.is_2fa_enabled, u.is_monetization_enabled,
                       u.reader_fear_rank, u.creator_fear_rank, u.creator_rank_score, u.reader_rank_score,
                       u.state, u.city, u.pincode, u.preferred_language,
                       u.total_followers, u.total_following, u.total_stories_published,
                       u.total_views_received, u.total_coins_earned, u.total_coins_spent,
                       u.login_streak, u.max_login_streak, u.total_reading_time_minutes,
                       u.referral_code, u.total_referrals, u.onboarding_completed,
                       u.created_at, u.last_active_at,
                       COALESCE(w.coin_balance, 0) as coin_balance
                FROM users u
                LEFT JOIN wallets w ON w.user_id = u.id
                WHERE u.id = @id AND u.deleted_at IS NULL", conn);
            cmd.Parameters.AddWithValue("@id", userId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                profile = MapToMyProfile(reader);
        }

        return profile;
    }

    // ─── UPDATE PROFILE ───────────────────────────────────────────────────────
    public async Task<(bool Success, string Message)> UpdateProfileAsync(Guid userId, UpdateProfileRequest req)
    {
        var updates = new List<string>();
        var params_ = new Dictionary<string, object?> { ["@id"] = userId };

        if (req.DisplayName != null) { updates.Add("display_name=@dn"); params_["@dn"] = req.DisplayName; }
        if (req.Bio != null) { updates.Add("bio=@bio"); params_["@bio"] = req.Bio; }
        if (req.Phone != null) { updates.Add("phone=@phone"); params_["@phone"] = req.Phone; }
        if (req.State != null) { updates.Add("state=@state"); params_["@state"] = req.State; }
        if (req.City != null) { updates.Add("city=@city"); params_["@city"] = req.City; }
        if (req.Pincode != null) { updates.Add("pincode=@pin"); params_["@pin"] = req.Pincode; }
        if (req.DateOfBirth != null) { updates.Add("date_of_birth=@dob"); params_["@dob"] = req.DateOfBirth; }

        if (req.Gender != null)
        {
            var validGenders = new[] { "male", "female", "other", "prefer_not_to_say" };
            if (!validGenders.Contains(req.Gender.ToLower()))
                return (false, "Invalid gender value");
            updates.Add("gender=@gender::gender_type");
            params_["@gender"] = req.Gender.ToLower();
        }

        if (req.PreferredLanguage != null)
        {
            var validLangs = new[] { "hindi", "hinglish", "english" };
            if (!validLangs.Contains(req.PreferredLanguage.ToLower()))
                return (false, "Invalid language. Use: hindi, hinglish, english");
            updates.Add("preferred_language=@lang::content_language");
            params_["@lang"] = req.PreferredLanguage.ToLower();
        }

        if (updates.Count == 0) return (false, "Kuch bhi update nahi kiya");

        updates.Add("updated_at=NOW()");

        await using var conn = await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(conn,
            $"UPDATE users SET {string.Join(", ", updates)} WHERE id=@id",
            params_);

        return (true, "Profile update ho gaya!");
    }

    // ─── UPDATE AVATAR ────────────────────────────────────────────────────────
    public async Task<(bool Success, string Message, string? AvatarUrl)> UpdateAvatarAsync(
        Guid userId, IFormFile file)
    {
        var result = await SaveImageAsync(file, "avatars", userId.ToString());
        if (!result.Success) return (false, result.Message, null);

        await using var conn = await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE users SET avatar_url=@url, updated_at=NOW() WHERE id=@id",
            new() { ["@url"] = result.Url, ["@id"] = userId });

        return (true, "Avatar update ho gaya!", result.Url);
    }

    // ─── UPDATE COVER IMAGE ───────────────────────────────────────────────────
    public async Task<(bool Success, string Message, string? CoverUrl)> UpdateCoverImageAsync(
        Guid userId, IFormFile file)
    {
        var result = await SaveImageAsync(file, "covers", userId.ToString());
        if (!result.Success) return (false, result.Message, null);

        await using var conn = await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE users SET cover_image_url=@url, updated_at=NOW() WHERE id=@id",
            new() { ["@url"] = result.Url, ["@id"] = userId });

        return (true, "Cover image update ho gaya!", result.Url);
    }

    // ─── DELETE ACCOUNT ───────────────────────────────────────────────────────
    public async Task<(bool Success, string Message)> DeleteAccountAsync(Guid userId)
    {
        await using var conn = await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE users SET deleted_at=NOW(), status='deactivated'::user_status, updated_at=NOW() WHERE id=@id",
            new() { ["@id"] = userId });

        // Sabhi sessions invalidate karo
        await DbHelper.ExecuteNonQueryAsync(conn,
            "UPDATE user_sessions SET is_active=FALSE WHERE user_id=@id",
            new() { ["@id"] = userId });

        return (true, "Account delete ho gaya.");
    }

    // ─── FOLLOW USER ──────────────────────────────────────────────────────────
    public async Task<(bool Success, string Message)> FollowUserAsync(Guid followerId, Guid targetId)
    {
        if (followerId == targetId)
            return (false, "Khud ko follow nahi kar sakte");

        await using var conn = await _db.CreateConnectionAsync();

        // Check target exists
        var targetExists = await DbHelper.ExecuteScalarAsync<int>(conn,
            "SELECT COUNT(1) FROM users WHERE id=@id AND deleted_at IS NULL AND status='active'::user_status",
            new() { ["@id"] = targetId });
        if (targetExists == 0) return (false, "User nahi mila");

        // Check block
        var blocked = await DbHelper.ExecuteScalarAsync<int>(conn,
            "SELECT COUNT(1) FROM blocks WHERE (blocker_id=@a AND blocked_id=@b) OR (blocker_id=@b AND blocked_id=@a)",
            new() { ["@a"] = followerId, ["@b"] = targetId });
        if (blocked > 0) return (false, "Follow nahi kar sakte");

        // Check already following
        var alreadyFollowing = await DbHelper.ExecuteScalarAsync<int>(conn,
            "SELECT COUNT(1) FROM follows WHERE follower_id=@fid AND following_id=@tid",
            new() { ["@fid"] = followerId, ["@tid"] = targetId });
        if (alreadyFollowing > 0) return (false, "Already follow kar rahe ho");

        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            await DbHelper.ExecuteNonQueryAsync(conn,
                "INSERT INTO follows (id, follower_id, following_id, created_at) VALUES (uuid_generate_v4(), @fid, @tid, NOW())",
                new() { ["@fid"] = followerId, ["@tid"] = targetId }, tx);

            // Update counts
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE users SET total_following=total_following+1 WHERE id=@id",
                new() { ["@id"] = followerId }, tx);
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE users SET total_followers=total_followers+1 WHERE id=@id",
                new() { ["@id"] = targetId }, tx);

            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { }
            _logger.LogError(ex, "Follow failed");
            return (false, "Follow failed");
        }

        return (true, "Follow kar liya!");
    }

    // ─── UNFOLLOW USER ────────────────────────────────────────────────────────
    public async Task<(bool Success, string Message)> UnfollowUserAsync(Guid followerId, Guid targetId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        var exists = await DbHelper.ExecuteScalarAsync<int>(conn,
            "SELECT COUNT(1) FROM follows WHERE follower_id=@fid AND following_id=@tid",
            new() { ["@fid"] = followerId, ["@tid"] = targetId });
        if (exists == 0) return (false, "Follow nahi kar rahe the");

        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            await DbHelper.ExecuteNonQueryAsync(conn,
                "DELETE FROM follows WHERE follower_id=@fid AND following_id=@tid",
                new() { ["@fid"] = followerId, ["@tid"] = targetId }, tx);

            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE users SET total_following=GREATEST(total_following-1,0) WHERE id=@id",
                new() { ["@id"] = followerId }, tx);
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE users SET total_followers=GREATEST(total_followers-1,0) WHERE id=@id",
                new() { ["@id"] = targetId }, tx);

            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { }
            _logger.LogError(ex, "Unfollow failed");
            return (false, "Unfollow failed");
        }

        return (true, "Unfollow ho gaya!");
    }

    // ─── GET FOLLOWERS ────────────────────────────────────────────────────────
    public async Task<PagedResult<FollowUserResponse>> GetFollowersAsync(
        Guid userId, Guid? viewerId, PaginationParams pagination)
    {
        var results = new List<FollowUserResponse>();
        int totalCount = 0;

        await using (var conn = await _db.CreateConnectionAsync())
        {
            totalCount = await DbHelper.ExecuteScalarAsync<int>(conn,
                "SELECT COUNT(1) FROM follows WHERE following_id=@uid",
                new() { ["@uid"] = userId });

            using var cmd = new NpgsqlCommand(@"
                SELECT u.id, u.username, u.display_name, u.avatar_url, u.is_creator,
                       u.is_verified_creator, u.reader_fear_rank, u.creator_fear_rank,
                       u.total_followers, f.created_at as followed_at
                FROM follows f
                JOIN users u ON u.id = f.follower_id
                WHERE f.following_id = @uid AND u.deleted_at IS NULL
                ORDER BY f.created_at DESC
                LIMIT @limit OFFSET @offset", conn);
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@limit", pagination.PageSize);
            cmd.Parameters.AddWithValue("@offset", pagination.Offset);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                results.Add(MapToFollowUser(reader));
        }

        // Is viewer following these users?
        if (viewerId.HasValue && results.Count > 0)
        {
            await using var conn2 = await _db.CreateConnectionAsync();
            foreach (var r in results)
            {
                var isFollowing = await DbHelper.ExecuteScalarAsync<int>(conn2,
                    "SELECT COUNT(1) FROM follows WHERE follower_id=@fid AND following_id=@tid",
                    new() { ["@fid"] = viewerId, ["@tid"] = r.Id });
                r.IsFollowing = isFollowing > 0;
            }
        }

        return new PagedResult<FollowUserResponse>
        {
            Items = results, TotalCount = totalCount,
            Page = pagination.Page, PageSize = pagination.PageSize
        };
    }

    // ─── GET FOLLOWING ────────────────────────────────────────────────────────
    public async Task<PagedResult<FollowUserResponse>> GetFollowingAsync(
        Guid userId, Guid? viewerId, PaginationParams pagination)
    {
        var results = new List<FollowUserResponse>();
        int totalCount = 0;

        await using (var conn = await _db.CreateConnectionAsync())
        {
            totalCount = await DbHelper.ExecuteScalarAsync<int>(conn,
                "SELECT COUNT(1) FROM follows WHERE follower_id=@uid",
                new() { ["@uid"] = userId });

            using var cmd = new NpgsqlCommand(@"
                SELECT u.id, u.username, u.display_name, u.avatar_url, u.is_creator,
                       u.is_verified_creator, u.reader_fear_rank, u.creator_fear_rank,
                       u.total_followers, f.created_at as followed_at
                FROM follows f
                JOIN users u ON u.id = f.following_id
                WHERE f.follower_id = @uid AND u.deleted_at IS NULL
                ORDER BY f.created_at DESC
                LIMIT @limit OFFSET @offset", conn);
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@limit", pagination.PageSize);
            cmd.Parameters.AddWithValue("@offset", pagination.Offset);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                results.Add(MapToFollowUser(reader));
        }

        if (viewerId.HasValue && results.Count > 0)
        {
            await using var conn2 = await _db.CreateConnectionAsync();
            foreach (var r in results)
            {
                var isFollowing = await DbHelper.ExecuteScalarAsync<int>(conn2,
                    "SELECT COUNT(1) FROM follows WHERE follower_id=@fid AND following_id=@tid",
                    new() { ["@fid"] = viewerId, ["@tid"] = r.Id });
                r.IsFollowing = isFollowing > 0;
            }
        }

        return new PagedResult<FollowUserResponse>
        {
            Items = results, TotalCount = totalCount,
            Page = pagination.Page, PageSize = pagination.PageSize
        };
    }

    // ─── BLOCK USER ───────────────────────────────────────────────────────────
    public async Task<(bool Success, string Message)> BlockUserAsync(Guid blockerId, Guid targetId)
    {
        if (blockerId == targetId) return (false, "Khud ko block nahi kar sakte");

        await using var conn = await _db.CreateConnectionAsync();

        var alreadyBlocked = await DbHelper.ExecuteScalarAsync<int>(conn,
            "SELECT COUNT(1) FROM blocks WHERE blocker_id=@bid AND blocked_id=@tid",
            new() { ["@bid"] = blockerId, ["@tid"] = targetId });
        if (alreadyBlocked > 0) return (false, "Already block hai");

        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            await DbHelper.ExecuteNonQueryAsync(conn,
                "INSERT INTO blocks (id, blocker_id, blocked_id, created_at) VALUES (uuid_generate_v4(), @bid, @tid, NOW())",
                new() { ["@bid"] = blockerId, ["@tid"] = targetId }, tx);

            // Dono taraf se follow hatao
            await DbHelper.ExecuteNonQueryAsync(conn,
                "DELETE FROM follows WHERE (follower_id=@a AND following_id=@b) OR (follower_id=@b AND following_id=@a)",
                new() { ["@a"] = blockerId, ["@b"] = targetId }, tx);

            // Counts update
            await DbHelper.ExecuteNonQueryAsync(conn, @"
                UPDATE users SET total_following=GREATEST(total_following-1,0) WHERE id=@bid;
                UPDATE users SET total_followers=GREATEST(total_followers-1,0) WHERE id=@tid;",
                new() { ["@bid"] = blockerId, ["@tid"] = targetId }, tx);

            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { }
            _logger.LogError(ex, "Block failed");
            return (false, "Block failed");
        }

        return (true, "User block ho gaya!");
    }

    // ─── UNBLOCK USER ─────────────────────────────────────────────────────────
    public async Task<(bool Success, string Message)> UnblockUserAsync(Guid blockerId, Guid targetId)
    {
        await using var conn = await _db.CreateConnectionAsync();

        var exists = await DbHelper.ExecuteScalarAsync<int>(conn,
            "SELECT COUNT(1) FROM blocks WHERE blocker_id=@bid AND blocked_id=@tid",
            new() { ["@bid"] = blockerId, ["@tid"] = targetId });
        if (exists == 0) return (false, "Blocked nahi tha");

        await DbHelper.ExecuteNonQueryAsync(conn,
            "DELETE FROM blocks WHERE blocker_id=@bid AND blocked_id=@tid",
            new() { ["@bid"] = blockerId, ["@tid"] = targetId });

        return (true, "Unblock ho gaya!");
    }

    // ─── GET BLOCKED USERS ────────────────────────────────────────────────────
    public async Task<PagedResult<FollowUserResponse>> GetBlockedUsersAsync(
        Guid userId, PaginationParams pagination)
    {
        var results = new List<FollowUserResponse>();
        int totalCount = 0;

        await using var conn = await _db.CreateConnectionAsync();

        totalCount = await DbHelper.ExecuteScalarAsync<int>(conn,
            "SELECT COUNT(1) FROM blocks WHERE blocker_id=@uid",
            new() { ["@uid"] = userId });

        using var cmd = new NpgsqlCommand(@"
            SELECT u.id, u.username, u.display_name, u.avatar_url, u.is_creator,
                   u.is_verified_creator, u.reader_fear_rank, u.creator_fear_rank,
                   u.total_followers, b.created_at as followed_at
            FROM blocks b
            JOIN users u ON u.id = b.blocked_id
            WHERE b.blocker_id = @uid AND u.deleted_at IS NULL
            ORDER BY b.created_at DESC
            LIMIT @limit OFFSET @offset", conn);
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@limit", pagination.PageSize);
        cmd.Parameters.AddWithValue("@offset", pagination.Offset);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(MapToFollowUser(reader));

        return new PagedResult<FollowUserResponse>
        {
            Items = results, TotalCount = totalCount,
            Page = pagination.Page, PageSize = pagination.PageSize
        };
    }

    public async Task<bool> IsBlockedAsync(Guid userId, Guid targetId)
    {
        await using var conn = await _db.CreateConnectionAsync();
        var count = await DbHelper.ExecuteScalarAsync<int>(conn,
            "SELECT COUNT(1) FROM blocks WHERE (blocker_id=@a AND blocked_id=@b) OR (blocker_id=@b AND blocked_id=@a)",
            new() { ["@a"] = userId, ["@b"] = targetId });
        return count > 0;
    }

    // ─── Image Save Helper ────────────────────────────────────────────────────
    private async Task<(bool Success, string Message, string Url)> SaveImageAsync(
        IFormFile file, string folder, string fileName)
    {
        // Validate
        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowedTypes.Contains(file.ContentType.ToLower()))
            return (false, "Sirf JPG, PNG, WebP allowed hai", "");

        if (file.Length > 5 * 1024 * 1024) // 5MB
            return (false, "Image 5MB se chhoti honi chahiye", "");

        // Save path
        var uploadsFolder = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", folder);
        Directory.CreateDirectory(uploadsFolder);

        var ext = Path.GetExtension(file.FileName).ToLower();
        var uniqueName = $"{fileName}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}{ext}";
        var filePath = Path.Combine(uploadsFolder, uniqueName);

        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);

        var url = $"/uploads/{folder}/{uniqueName}";
        return (true, "Image save ho gaya", url);
    }

    // ─── Mappers ──────────────────────────────────────────────────────────────
    private UserProfileResponse MapToPublicProfile(NpgsqlDataReader r) => new()
    {
        Id                   = DbHelper.GetGuid(r, "id"),
        Username             = DbHelper.GetString(r, "username"),
        DisplayName          = DbHelper.GetStringOrNull(r, "display_name"),
        Bio                  = DbHelper.GetStringOrNull(r, "bio"),
        AvatarUrl            = DbHelper.GetStringOrNull(r, "avatar_url"),
        CoverImageUrl        = DbHelper.GetStringOrNull(r, "cover_image_url"),
        Role                 = DbHelper.GetString(r, "role"),
        IsCreator            = DbHelper.GetBool(r, "is_creator"),
        IsVerifiedCreator    = DbHelper.GetBool(r, "is_verified_creator"),
        IsEmailVerified      = DbHelper.GetBool(r, "is_email_verified"),
        ReaderFearRank       = DbHelper.GetString(r, "reader_fear_rank"),
        CreatorFearRank      = DbHelper.GetStringOrNull(r, "creator_fear_rank"),
        CreatorRankScore     = DbHelper.GetDecimal(r, "creator_rank_score"),
        ReaderRankScore      = DbHelper.GetDecimal(r, "reader_rank_score"),
        State                = DbHelper.GetStringOrNull(r, "state"),
        City                 = DbHelper.GetStringOrNull(r, "city"),
        PreferredLanguage    = DbHelper.GetString(r, "preferred_language"),
        TotalFollowers       = DbHelper.GetInt(r, "total_followers"),
        TotalFollowing       = DbHelper.GetInt(r, "total_following"),
        TotalStoriesPublished = DbHelper.GetInt(r, "total_stories_published"),
        TotalViewsReceived   = DbHelper.GetLong(r, "total_views_received"),
        TotalCoinsEarned     = DbHelper.GetLong(r, "total_coins_earned"),
        LoginStreak          = DbHelper.GetInt(r, "login_streak"),
        ReferralCode         = DbHelper.GetStringOrNull(r, "referral_code"),
        CreatedAt            = DbHelper.GetDateTime(r, "created_at"),
        LastActiveAt         = DbHelper.GetDateTimeOrNull(r, "last_active_at")
    };

    private MyProfileResponse MapToMyProfile(NpgsqlDataReader r) => new()
    {
        Id                      = DbHelper.GetGuid(r, "id"),
        Username                = DbHelper.GetString(r, "username"),
        Email                   = DbHelper.GetString(r, "email"),
        DisplayName             = DbHelper.GetStringOrNull(r, "display_name"),
        Bio                     = DbHelper.GetStringOrNull(r, "bio"),
        AvatarUrl               = DbHelper.GetStringOrNull(r, "avatar_url"),
        CoverImageUrl           = DbHelper.GetStringOrNull(r, "cover_image_url"),
        Phone                   = DbHelper.GetStringOrNull(r, "phone"),
        Gender                  = DbHelper.GetStringOrNull(r, "gender"),
        DateOfBirth             = DbHelper.GetDateTimeOrNull(r, "date_of_birth"),
        Role                    = DbHelper.GetString(r, "role"),
        IsCreator               = DbHelper.GetBool(r, "is_creator"),
        IsVerifiedCreator       = DbHelper.GetBool(r, "is_verified_creator"),
        IsEmailVerified         = DbHelper.GetBool(r, "is_email_verified"),
        Is2FAEnabled            = DbHelper.GetBool(r, "is_2fa_enabled"),
        IsMonetizationEnabled   = DbHelper.GetBool(r, "is_monetization_enabled"),
        ReaderFearRank          = DbHelper.GetString(r, "reader_fear_rank"),
        CreatorFearRank         = DbHelper.GetStringOrNull(r, "creator_fear_rank"),
        CreatorRankScore        = DbHelper.GetDecimal(r, "creator_rank_score"),
        ReaderRankScore         = DbHelper.GetDecimal(r, "reader_rank_score"),
        State                   = DbHelper.GetStringOrNull(r, "state"),
        City                    = DbHelper.GetStringOrNull(r, "city"),
        Pincode                 = DbHelper.GetStringOrNull(r, "pincode"),
        PreferredLanguage       = DbHelper.GetString(r, "preferred_language"),
        TotalFollowers          = DbHelper.GetInt(r, "total_followers"),
        TotalFollowing          = DbHelper.GetInt(r, "total_following"),
        TotalStoriesPublished   = DbHelper.GetInt(r, "total_stories_published"),
        TotalViewsReceived      = DbHelper.GetLong(r, "total_views_received"),
        TotalCoinsEarned        = DbHelper.GetLong(r, "total_coins_earned"),
        TotalCoinsSpent         = DbHelper.GetLong(r, "total_coins_spent"),
        LoginStreak             = DbHelper.GetInt(r, "login_streak"),
        MaxLoginStreak          = DbHelper.GetInt(r, "max_login_streak"),
        TotalReadingTimeMinutes = DbHelper.GetLong(r, "total_reading_time_minutes"),
        ReferralCode            = DbHelper.GetStringOrNull(r, "referral_code"),
        TotalReferrals          = DbHelper.GetInt(r, "total_referrals"),
        OnboardingCompleted     = DbHelper.GetBool(r, "onboarding_completed"),
        CreatedAt               = DbHelper.GetDateTime(r, "created_at"),
        LastActiveAt            = DbHelper.GetDateTimeOrNull(r, "last_active_at"),
        CoinBalance             = DbHelper.GetLong(r, "coin_balance")
    };

    private FollowUserResponse MapToFollowUser(NpgsqlDataReader r) => new()
    {
        Id               = DbHelper.GetGuid(r, "id"),
        Username         = DbHelper.GetString(r, "username"),
        DisplayName      = DbHelper.GetStringOrNull(r, "display_name"),
        AvatarUrl        = DbHelper.GetStringOrNull(r, "avatar_url"),
        IsCreator        = DbHelper.GetBool(r, "is_creator"),
        IsVerifiedCreator = DbHelper.GetBool(r, "is_verified_creator"),
        ReaderFearRank   = DbHelper.GetString(r, "reader_fear_rank"),
        CreatorFearRank  = DbHelper.GetStringOrNull(r, "creator_fear_rank"),
        TotalFollowers   = DbHelper.GetInt(r, "total_followers"),
        FollowedAt       = DbHelper.GetDateTime(r, "followed_at")
    };

    // ─── SEARCH USERS ─────────────────────────────────────────────────────────
    public async Task<List<FollowUserResponse>> SearchUsersAsync(string q, int page, int pageSize)
    {
        if (string.IsNullOrWhiteSpace(q)) return [];
        await using var conn = await _db.CreateConnectionAsync();
        var pattern = $"%{q.Trim().ToLower()}%";
        var offset = (page - 1) * pageSize;

        return await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT id, username, display_name, avatar_url, is_creator, is_verified_creator,
                     reader_fear_rank, creator_fear_rank, total_followers,
                     NOW() as followed_at
              FROM users
              WHERE deleted_at IS NULL
                AND (LOWER(username) LIKE @q OR LOWER(display_name) LIKE @q)
              ORDER BY total_followers DESC NULLS LAST, created_at ASC
              LIMIT @lim OFFSET @off",
            MapToFollowUser,
            new Dictionary<string, object?> { ["@q"] = pattern, ["@lim"] = pageSize, ["@off"] = offset });
    }
}