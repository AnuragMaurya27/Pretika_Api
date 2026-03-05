using Google.Apis.Auth;
using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Infrastructure.Database;
using HauntedVoiceUniverse.Modules.Auth.Models;
using Npgsql;
using OtpNet;

namespace HauntedVoiceUniverse.Modules.Auth.Services;

public interface IAuthService
{
    Task<(bool Success, string Message, AuthResponse? Data)> RegisterAsync(RegisterRequest req, string ipAddress);
    Task<(bool Success, string Message, AuthResponse? Data)> LoginAsync(LoginRequest req, string ipAddress, string? userAgent);
    Task<(bool Success, string Message, AuthResponse? Data)> GoogleLoginAsync(GoogleLoginRequest req, string ipAddress);
    Task<(bool Success, string Message)> VerifyEmailAsync(string token);
    Task<(bool Success, string Message)> ForgotPasswordAsync(string email);
    Task<(bool Success, string Message)> ResetPasswordAsync(ResetPasswordRequest req);
    Task<(bool Success, string Message)> ChangePasswordAsync(Guid userId, ChangePasswordRequest req);
    Task<(bool Success, string Message, AuthResponse? Data)> RefreshTokenAsync(string refreshToken, string ipAddress);
    Task<(bool Success, string Message)> LogoutAsync(Guid userId, string refreshToken);
    Task<(bool Success, string Message, TwoFASetupResponse? Data)> Setup2FAAsync(Guid userId);
    Task<(bool Success, string Message)> Enable2FAAsync(Guid userId, string otpCode);
    Task<(bool Success, string Message)> Disable2FAAsync(Guid userId, string otpCode);
    Task<bool> ResendVerificationEmailAsync(string email);
}

public class AuthService : IAuthService
{
    private readonly IDbConnectionFactory _db;
    private readonly IJwtService _jwtService;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IDbConnectionFactory db, IJwtService jwtService,
        IEmailService emailService, IConfiguration config, ILogger<AuthService> logger)
    {
        _db = db; _jwtService = jwtService; _emailService = emailService;
        _config = config; _logger = logger;
    }

    public async Task<(bool Success, string Message, AuthResponse? Data)> RegisterAsync(
        RegisterRequest req, string ipAddress)
    {
        await using var conn = await _db.CreateConnectionAsync();
        var exists = await DbHelper.ExecuteScalarAsync<int>(conn,
            "SELECT COUNT(1) FROM users WHERE LOWER(email)=LOWER(@email) OR LOWER(username)=LOWER(@username)",
            new() { ["@email"] = req.Email, ["@username"] = req.Username });
        if (exists > 0) return (false, "Email ya username already use ho raha hai", null);

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(req.Password, 12);
        var referralCode = GenerateReferralCode(req.Username);
        Guid? referredBy = null;
        if (!string.IsNullOrWhiteSpace(req.ReferralCode))
            referredBy = await DbHelper.ExecuteScalarAsync<Guid?>(conn,
                "SELECT id FROM users WHERE referral_code=@code",
                new() { ["@code"] = req.ReferralCode.Trim().ToUpper() });

        var userId = Guid.NewGuid();
        var signupBonus = int.Parse(_config["CoinSettings:SignupBonusCoins"] ?? "50");
        await using var transaction = await conn.BeginTransactionAsync();
        bool committed = false;
        string verifyToken = "";
        try
        {
            await DbHelper.ExecuteNonQueryAsync(conn, @"
                INSERT INTO users (id,username,email,password_hash,display_name,preferred_language,
                    referral_code,referred_by,role,status,is_email_verified,created_at,updated_at)
                VALUES (@id,@username,@email,@passwordHash,@displayName,@language::content_language,
                    @referralCode,@referredBy,'reader'::user_role,'active'::user_status,FALSE,NOW(),NOW())",
                new() { ["@id"]=userId, ["@username"]=req.Username.Trim(), ["@email"]=req.Email.Trim().ToLower(),
                    ["@passwordHash"]=passwordHash, ["@displayName"]=req.DisplayName??req.Username,
                    ["@language"]=req.PreferredLanguage??"hindi", ["@referralCode"]=referralCode,
                    ["@referredBy"]=(object?)referredBy??DBNull.Value }, transaction);

            await DbHelper.ExecuteNonQueryAsync(conn, @"
                INSERT INTO wallets (id,user_id,coin_balance,total_earned,total_spent,created_at,updated_at)
                VALUES (uuid_generate_v4(),@userId,0,0,0,NOW(),NOW()) ON CONFLICT (user_id) DO NOTHING",
                new() { ["@userId"]=userId }, transaction);

            if (signupBonus > 0)
            {
                await DbHelper.ExecuteNonQueryAsync(conn,
                    "UPDATE wallets SET coin_balance=coin_balance+@coins,updated_at=NOW() WHERE user_id=@userId",
                    new() { ["@coins"]=signupBonus, ["@userId"]=userId }, transaction);
                await DbHelper.ExecuteNonQueryAsync(conn, @"
                    INSERT INTO coin_transactions (id,receiver_id,transaction_type,amount,description,status,created_at)
                    VALUES (uuid_generate_v4(),@userId,'signup_bonus'::transaction_type,@coins,'Welcome signup bonus','completed'::transaction_status,NOW())",
                    new() { ["@userId"]=userId, ["@coins"]=signupBonus }, transaction);
            }

            if (referredBy.HasValue)
            {
                await DbHelper.ExecuteNonQueryAsync(conn,
                    "UPDATE wallets SET coin_balance=coin_balance+100,updated_at=NOW() WHERE user_id=@uid",
                    new() { ["@uid"]=referredBy }, transaction);
                await DbHelper.ExecuteNonQueryAsync(conn,
                    "UPDATE users SET total_referrals=total_referrals+1 WHERE id=@uid",
                    new() { ["@uid"]=referredBy }, transaction);
            }

            verifyToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            await DbHelper.ExecuteNonQueryAsync(conn, @"
                INSERT INTO email_verification_tokens (id,user_id,token,expires_at,created_at)
                VALUES (uuid_generate_v4(),@userId,@token,NOW()+INTERVAL '24 hours',NOW())",
                new() { ["@userId"]=userId, ["@token"]=verifyToken }, transaction);

            await transaction.CommitAsync();
            committed = true;
        }
        catch (Exception ex)
        {
            if (!committed) try { await transaction.RollbackAsync(); } catch { }
            _logger.LogError(ex, "Register failed {Email}: {Msg}", req.Email, ex.Message);
            return (false, $"Registration failed: {ex.Message}", null);
        }

        try { await _emailService.SendEmailVerificationAsync(req.Email, req.Username, verifyToken); } catch { }
        _ = LogLoginAsync(userId, ipAddress, "email", true);

        var userInfo = new UserAuthInfo { Id=userId, Username=req.Username, Email=req.Email.ToLower(),
            DisplayName=req.DisplayName??req.Username, Role="reader", IsCreator=false,
            IsEmailVerified=false, Is2FAEnabled=false, ReaderFearRank="raat_ka_musafir", CoinBalance=signupBonus };

        var at = _jwtService.GenerateAccessToken(userInfo);
        var rt = _jwtService.GenerateRefreshToken();
        await SaveRefreshTokenAsync(userId, rt, ipAddress);

        return (true, "Registration successful! Email verify karo.", new AuthResponse {
            AccessToken=at, RefreshToken=rt,
            AccessTokenExpiry=DateTime.UtcNow.AddMinutes(60),
            RefreshTokenExpiry=DateTime.UtcNow.AddDays(30), User=userInfo });
    }

    public async Task<(bool Success, string Message, AuthResponse? Data)> LoginAsync(
        LoginRequest req, string ipAddress, string? userAgent)
    {
        UserAuthInfo? userInfo = null;
        string? passwordHash=null, status=null, twoFaSecret=null;
        bool is2FAEnabled = false;
        Guid userId = Guid.Empty;

        await using (var readConn = await _db.CreateConnectionAsync())
        {
            using var cmd = new NpgsqlCommand(@"
                SELECT u.id,u.username,u.email,u.password_hash,u.display_name,u.avatar_url,u.role,
                       u.status,u.is_email_verified,u.is_2fa_enabled,u.two_fa_secret,u.is_creator,
                       u.reader_fear_rank,u.creator_fear_rank,COALESCE(w.coin_balance,0) as coin_balance
                FROM users u LEFT JOIN wallets w ON w.user_id=u.id
                WHERE (LOWER(u.email)=LOWER(@input) OR LOWER(u.username)=LOWER(@input))
                  AND u.deleted_at IS NULL LIMIT 1", readConn);
            cmd.Parameters.AddWithValue("@input", req.EmailOrUsername);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                userId=DbHelper.GetGuid(reader,"id"); status=DbHelper.GetString(reader,"status");
                passwordHash=DbHelper.GetStringOrNull(reader,"password_hash");
                is2FAEnabled=DbHelper.GetBool(reader,"is_2fa_enabled");
                twoFaSecret=DbHelper.GetStringOrNull(reader,"two_fa_secret");
                userInfo = new UserAuthInfo {
                    Id=userId, Username=DbHelper.GetString(reader,"username"),
                    Email=DbHelper.GetString(reader,"email"),
                    DisplayName=DbHelper.GetStringOrNull(reader,"display_name"),
                    AvatarUrl=DbHelper.GetStringOrNull(reader,"avatar_url"),
                    Role=DbHelper.GetString(reader,"role"),
                    IsCreator=DbHelper.GetBool(reader,"is_creator"),
                    IsEmailVerified=DbHelper.GetBool(reader,"is_email_verified"),
                    Is2FAEnabled=is2FAEnabled,
                    ReaderFearRank=DbHelper.GetString(reader,"reader_fear_rank"),
                    CreatorFearRank=DbHelper.GetStringOrNull(reader,"creator_fear_rank"),
                    CoinBalance=DbHelper.GetLong(reader,"coin_balance") };
            }
        }

        if (userInfo == null) return (false, "Email/username ya password galat hai", null);
        if (status=="banned"||status=="deactivated") return (false, "Account ban/deactivate hai", null);
        if (status=="suspended") return (false, "Account suspended hai", null);
        if (string.IsNullOrEmpty(passwordHash)||!BCrypt.Net.BCrypt.Verify(req.Password, passwordHash))
            return (false, "Email/username ya password galat hai", null);
        if (is2FAEnabled) {
            if (string.IsNullOrWhiteSpace(req.TwoFaCode)) return (false, "2FA code required hai", null);
            if (!Verify2FA(twoFaSecret, req.TwoFaCode)) return (false, "2FA code galat hai", null);
        }

        await using var writeConn = await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(writeConn,
            "UPDATE users SET last_login_at=NOW(),last_active_at=NOW() WHERE id=@id",
            new() { ["@id"]=userId });
        _ = LogLoginAsync(userId, ipAddress, "email", true);

        var at = _jwtService.GenerateAccessToken(userInfo);
        var rt = _jwtService.GenerateRefreshToken();
        await SaveRefreshTokenAsync(userId, rt, ipAddress, userAgent);

        return (true, "Login successful", new AuthResponse {
            AccessToken=at, RefreshToken=rt,
            AccessTokenExpiry=DateTime.UtcNow.AddMinutes(60),
            RefreshTokenExpiry=DateTime.UtcNow.AddDays(30), User=userInfo });
    }

    public async Task<(bool Success, string Message, AuthResponse? Data)> GoogleLoginAsync(
        GoogleLoginRequest req, string ipAddress)
    {
        GoogleJsonWebSignature.Payload payload;
        try { payload = await GoogleJsonWebSignature.ValidateAsync(req.IdToken,
            new GoogleJsonWebSignature.ValidationSettings { Audience = new[]{_config["Google:ClientId"]} }); }
        catch { return (false, "Invalid Google token", null); }

        Guid? existingUserId = null;
        await using (var c = await _db.CreateConnectionAsync())
            existingUserId = await DbHelper.ExecuteScalarAsync<Guid?>(c,
                "SELECT user_id FROM oauth_accounts WHERE provider='google'::oauth_provider AND provider_user_id=@gid",
                new() { ["@gid"]=payload.Subject });

        if (existingUserId.HasValue)
        {
            UserAuthInfo? eu = null;
            await using (var rc = await _db.CreateConnectionAsync())
            {
                using var cmd = new NpgsqlCommand(@"
                    SELECT u.id,u.username,u.email,u.display_name,u.avatar_url,u.role,u.status,
                           u.is_email_verified,u.is_2fa_enabled,u.is_creator,u.reader_fear_rank,
                           u.creator_fear_rank,COALESCE(w.coin_balance,0) as coin_balance
                    FROM users u LEFT JOIN wallets w ON w.user_id=u.id
                    WHERE u.id=@id AND u.deleted_at IS NULL", rc);
                cmd.Parameters.AddWithValue("@id", existingUserId.Value);
                using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                    eu = new UserAuthInfo { Id=DbHelper.GetGuid(r,"id"), Username=DbHelper.GetString(r,"username"),
                        Email=DbHelper.GetString(r,"email"), DisplayName=DbHelper.GetStringOrNull(r,"display_name"),
                        AvatarUrl=DbHelper.GetStringOrNull(r,"avatar_url"), Role=DbHelper.GetString(r,"role"),
                        IsCreator=DbHelper.GetBool(r,"is_creator"), IsEmailVerified=DbHelper.GetBool(r,"is_email_verified"),
                        Is2FAEnabled=DbHelper.GetBool(r,"is_2fa_enabled"), ReaderFearRank=DbHelper.GetString(r,"reader_fear_rank"),
                        CreatorFearRank=DbHelper.GetStringOrNull(r,"creator_fear_rank"), CoinBalance=DbHelper.GetLong(r,"coin_balance") };
            }
            if (eu==null) return (false, "Account issue hai", null);
            var at=_jwtService.GenerateAccessToken(eu); var rt=_jwtService.GenerateRefreshToken();
            await SaveRefreshTokenAsync(existingUserId.Value, rt, ipAddress);
            return (true,"Google login successful",new AuthResponse{AccessToken=at,RefreshToken=rt,
                AccessTokenExpiry=DateTime.UtcNow.AddMinutes(60),RefreshTokenExpiry=DateTime.UtcNow.AddDays(30),User=eu});
        }

        var newUserId = Guid.NewGuid();
        var baseUname = payload.Email.Split('@')[0].Replace(".","_").ToLower();
        string username;
        await using (var uc = await _db.CreateConnectionAsync())
            username = await GenerateUniqueUsernameAsync(uc, baseUname);

        await using var wc = await _db.CreateConnectionAsync();
        await using var tx = await wc.BeginTransactionAsync();
        bool committed = false;
        try
        {
            await DbHelper.ExecuteNonQueryAsync(wc, @"
                INSERT INTO users (id,username,email,display_name,avatar_url,role,status,is_email_verified,referral_code,created_at,updated_at)
                VALUES (@id,@u,@e,@dn,@av,'reader'::user_role,'active'::user_status,TRUE,@rc,NOW(),NOW())",
                new(){["@id"]=newUserId,["@u"]=username,["@e"]=payload.Email,["@dn"]=payload.Name??username,
                    ["@av"]=(object?)payload.Picture??DBNull.Value,["@rc"]=GenerateReferralCode(username)}, tx);
            await DbHelper.ExecuteNonQueryAsync(wc,
                "INSERT INTO oauth_accounts (id,user_id,provider,provider_user_id,provider_email,created_at,updated_at) VALUES (uuid_generate_v4(),@uid,'google'::oauth_provider,@gid,@e,NOW(),NOW())",
                new(){["@uid"]=newUserId,["@gid"]=payload.Subject,["@e"]=payload.Email}, tx);
            await DbHelper.ExecuteNonQueryAsync(wc,
                "INSERT INTO wallets (id,user_id,coin_balance,total_earned,total_spent,created_at,updated_at) VALUES (uuid_generate_v4(),@uid,50,0,0,NOW(),NOW())",
                new(){["@uid"]=newUserId}, tx);
            await tx.CommitAsync(); committed=true;
        }
        catch (Exception ex)
        {
            if (!committed) try{await tx.RollbackAsync();}catch{}
            _logger.LogError(ex,"Google login failed {E}",payload.Email);
            return (false,"Google login failed",null);
        }

        var nu=new UserAuthInfo{Id=newUserId,Username=username,Email=payload.Email,
            DisplayName=payload.Name,AvatarUrl=payload.Picture,Role="reader",IsCreator=false,
            IsEmailVerified=true,ReaderFearRank="raat_ka_musafir",CoinBalance=50};
        var nat=_jwtService.GenerateAccessToken(nu); var nrt=_jwtService.GenerateRefreshToken();
        await SaveRefreshTokenAsync(newUserId, nrt, ipAddress);
        return (true,"Google se register successful!",new AuthResponse{AccessToken=nat,RefreshToken=nrt,
            AccessTokenExpiry=DateTime.UtcNow.AddMinutes(60),RefreshTokenExpiry=DateTime.UtcNow.AddDays(30),User=nu});
    }

    public async Task<(bool Success, string Message)> VerifyEmailAsync(string token)
    {
        Guid tid=Guid.Empty,uid=Guid.Empty; bool notFound=false,used=false,expired=false;
        await using (var rc = await _db.CreateConnectionAsync())
        {
            using var cmd=new NpgsqlCommand("SELECT id,user_id,expires_at,used_at FROM email_verification_tokens WHERE token=@t",rc);
            cmd.Parameters.AddWithValue("@t",token);
            using var r=await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) notFound=true;
            else { tid=DbHelper.GetGuid(r,"id"); uid=DbHelper.GetGuid(r,"user_id");
                used=DbHelper.GetDateTimeOrNull(r,"used_at")!=null;
                expired=DbHelper.GetDateTime(r,"expires_at")<DateTime.UtcNow; }
        }
        if (notFound) return (false,"Token invalid hai");
        if (used) return (false,"Token already use ho chuka hai");
        if (expired) return (false,"Token expire ho gaya hai");
        await using var wc=await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(wc,"UPDATE users SET is_email_verified=TRUE WHERE id=@uid",new(){["@uid"]=uid});
        await DbHelper.ExecuteNonQueryAsync(wc,"UPDATE email_verification_tokens SET used_at=NOW() WHERE id=@tid",new(){["@tid"]=tid});
        return (true,"Email verify ho gaya! Ab fully login kar sakte ho.");
    }

    public async Task<(bool Success, string Message)> ForgotPasswordAsync(string email)
    {
        Guid uid=Guid.Empty; string uname=""; bool found=false;
        await using (var rc=await _db.CreateConnectionAsync())
        {
            using var cmd=new NpgsqlCommand("SELECT id,username FROM users WHERE LOWER(email)=LOWER(@e) AND deleted_at IS NULL",rc);
            cmd.Parameters.AddWithValue("@e",email);
            using var r=await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()){uid=DbHelper.GetGuid(r,"id");uname=DbHelper.GetString(r,"username");found=true;}
        }
        if (!found) return (true,"Agar email registered hai toh reset link bhej diya hai");
        await using var wc=await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(wc,"DELETE FROM password_reset_tokens WHERE user_id=@uid",new(){["@uid"]=uid});
        var rt=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N");
        await DbHelper.ExecuteNonQueryAsync(wc,"INSERT INTO password_reset_tokens (id,user_id,token,expires_at,created_at) VALUES (uuid_generate_v4(),@uid,@t,NOW()+INTERVAL '1 hour',NOW())",
            new(){["@uid"]=uid,["@t"]=rt});
        try{await _emailService.SendPasswordResetAsync(email,uname,rt);}catch{}
        return (true,"Password reset link email pe bhej diya hai");
    }

    public async Task<(bool Success, string Message)> ResetPasswordAsync(ResetPasswordRequest req)
    {
        Guid tid=Guid.Empty,uid=Guid.Empty; bool notFound=false,used=false,expired=false;
        await using (var rc=await _db.CreateConnectionAsync())
        {
            using var cmd=new NpgsqlCommand("SELECT id,user_id,expires_at,used_at FROM password_reset_tokens WHERE token=@t",rc);
            cmd.Parameters.AddWithValue("@t",req.Token);
            using var r=await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) notFound=true;
            else{tid=DbHelper.GetGuid(r,"id");uid=DbHelper.GetGuid(r,"user_id");
                used=DbHelper.GetDateTimeOrNull(r,"used_at")!=null;
                expired=DbHelper.GetDateTime(r,"expires_at")<DateTime.UtcNow;}
        }
        if (notFound) return (false,"Token invalid hai");
        if (used) return (false,"Token already use ho chuka");
        if (expired) return (false,"Token expire ho gaya");
        var newHash=BCrypt.Net.BCrypt.HashPassword(req.NewPassword,12);
        await using var wc=await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(wc,"UPDATE users SET password_hash=@h,updated_at=NOW() WHERE id=@uid",new(){["@h"]=newHash,["@uid"]=uid});
        await DbHelper.ExecuteNonQueryAsync(wc,"UPDATE password_reset_tokens SET used_at=NOW() WHERE id=@tid",new(){["@tid"]=tid});
        await DbHelper.ExecuteNonQueryAsync(wc,"UPDATE user_sessions SET is_active=FALSE WHERE user_id=@uid",new(){["@uid"]=uid});
        return (true,"Password reset ho gaya! Ab login karo.");
    }

    public async Task<(bool Success, string Message)> ChangePasswordAsync(Guid userId, ChangePasswordRequest req)
    {
        string? ch;
        await using (var rc=await _db.CreateConnectionAsync())
            ch=await DbHelper.ExecuteScalarAsync<string>(rc,"SELECT password_hash FROM users WHERE id=@id",new(){["@id"]=userId});
        if (string.IsNullOrEmpty(ch)||!BCrypt.Net.BCrypt.Verify(req.CurrentPassword,ch)) return (false,"Current password galat hai");
        var nh=BCrypt.Net.BCrypt.HashPassword(req.NewPassword,12);
        await using var wc=await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(wc,"UPDATE users SET password_hash=@h,updated_at=NOW() WHERE id=@id",new(){["@h"]=nh,["@id"]=userId});
        return (true,"Password change ho gaya!");
    }

    public async Task<(bool Success, string Message, AuthResponse? Data)> RefreshTokenAsync(
        string refreshToken, string ipAddress)
    {
        var th=_jwtService.HashToken(refreshToken);
        UserAuthInfo? ui=null; Guid sid=Guid.Empty; bool sexp=false,ainact=false;
        await using (var rc=await _db.CreateConnectionAsync())
        {
            using var cmd=new NpgsqlCommand(@"
                SELECT s.id,s.user_id,s.expires_at,s.is_active,u.username,u.email,u.role,u.status,
                       u.is_creator,u.display_name,u.avatar_url,u.reader_fear_rank,u.creator_fear_rank,
                       u.is_email_verified,u.is_2fa_enabled,COALESCE(w.coin_balance,0) as coin_balance
                FROM user_sessions s JOIN users u ON u.id=s.user_id
                LEFT JOIN wallets w ON w.user_id=u.id
                WHERE s.refresh_token_hash=@h AND s.is_active=TRUE",rc);
            cmd.Parameters.AddWithValue("@h",th);
            using var r=await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                sid=DbHelper.GetGuid(r,"id"); sexp=DbHelper.GetDateTime(r,"expires_at")<DateTime.UtcNow;
                ainact=DbHelper.GetString(r,"status")!="active";
                ui=new UserAuthInfo{Id=DbHelper.GetGuid(r,"user_id"),Username=DbHelper.GetString(r,"username"),
                    Email=DbHelper.GetString(r,"email"),DisplayName=DbHelper.GetStringOrNull(r,"display_name"),
                    AvatarUrl=DbHelper.GetStringOrNull(r,"avatar_url"),Role=DbHelper.GetString(r,"role"),
                    IsCreator=DbHelper.GetBool(r,"is_creator"),IsEmailVerified=DbHelper.GetBool(r,"is_email_verified"),
                    Is2FAEnabled=DbHelper.GetBool(r,"is_2fa_enabled"),ReaderFearRank=DbHelper.GetString(r,"reader_fear_rank"),
                    CreatorFearRank=DbHelper.GetStringOrNull(r,"creator_fear_rank"),CoinBalance=DbHelper.GetLong(r,"coin_balance")};
            }
        }
        if (ui==null) return (false,"Invalid refresh token",null);
        if (sexp) return (false,"Session expire ho gaya, dobara login karo",null);
        if (ainact) return (false,"Account active nahi hai",null);
        var nrt=_jwtService.GenerateRefreshToken(); var nh=_jwtService.HashToken(nrt);
        await using var wc=await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(wc,"UPDATE user_sessions SET refresh_token_hash=@nh,last_used_at=NOW(),expires_at=NOW()+INTERVAL '30 days' WHERE id=@sid",
            new(){["@nh"]=nh,["@sid"]=sid});
        var nat=_jwtService.GenerateAccessToken(ui);
        return (true,"Token refreshed",new AuthResponse{AccessToken=nat,RefreshToken=nrt,
            AccessTokenExpiry=DateTime.UtcNow.AddMinutes(60),RefreshTokenExpiry=DateTime.UtcNow.AddDays(30),User=ui});
    }

    public async Task<(bool Success, string Message)> LogoutAsync(Guid userId, string refreshToken)
    {
        var th=_jwtService.HashToken(refreshToken);
        await using var c=await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(c,"UPDATE user_sessions SET is_active=FALSE WHERE user_id=@uid AND refresh_token_hash=@h",
            new(){["@uid"]=userId,["@h"]=th});
        return (true,"Logout successful");
    }

    public async Task<(bool Success, string Message, TwoFASetupResponse? Data)> Setup2FAAsync(Guid userId)
    {
        string? email;
        await using (var rc=await _db.CreateConnectionAsync())
            email=await DbHelper.ExecuteScalarAsync<string>(rc,"SELECT email FROM users WHERE id=@id",new(){["@id"]=userId});
        var sk=Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
        var qr=$"otpauth://totp/HauntedVoiceUniverse:{email}?secret={sk}&issuer=HauntedVoiceUniverse";
        await using var wc=await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(wc,"UPDATE users SET two_fa_secret=@s WHERE id=@id",new(){["@s"]=sk,["@id"]=userId});
        return (true,"2FA setup ke liye QR scan karo",new TwoFASetupResponse{SecretKey=sk,QrCodeUrl=qr,ManualEntryCode=sk});
    }

    public async Task<(bool Success, string Message)> Enable2FAAsync(Guid userId, string otpCode)
    {
        string? s;
        await using (var rc=await _db.CreateConnectionAsync())
            s=await DbHelper.ExecuteScalarAsync<string>(rc,"SELECT two_fa_secret FROM users WHERE id=@id",new(){["@id"]=userId});
        if (!Verify2FA(s,otpCode)) return (false,"OTP code galat hai");
        await using var wc=await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(wc,"UPDATE users SET is_2fa_enabled=TRUE WHERE id=@id",new(){["@id"]=userId});
        return (true,"2FA enable ho gaya!");
    }

    public async Task<(bool Success, string Message)> Disable2FAAsync(Guid userId, string otpCode)
    {
        string? s;
        await using (var rc=await _db.CreateConnectionAsync())
            s=await DbHelper.ExecuteScalarAsync<string>(rc,"SELECT two_fa_secret FROM users WHERE id=@id",new(){["@id"]=userId});
        if (!Verify2FA(s,otpCode)) return (false,"OTP code galat hai");
        await using var wc=await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(wc,"UPDATE users SET is_2fa_enabled=FALSE,two_fa_secret=NULL WHERE id=@id",new(){["@id"]=userId});
        return (true,"2FA disable ho gaya.");
    }

    public async Task<bool> ResendVerificationEmailAsync(string email)
    {
        Guid uid=Guid.Empty; string uname=""; bool found=false;
        await using (var rc=await _db.CreateConnectionAsync())
        {
            using var cmd=new NpgsqlCommand("SELECT id,username,is_email_verified FROM users WHERE LOWER(email)=LOWER(@e)",rc);
            cmd.Parameters.AddWithValue("@e",email);
            using var r=await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()){
                if (DbHelper.GetBool(r,"is_email_verified")) return false;
                uid=DbHelper.GetGuid(r,"id"); uname=DbHelper.GetString(r,"username"); found=true;}
        }
        if (!found) return false;
        await using var wc=await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(wc,"DELETE FROM email_verification_tokens WHERE user_id=@uid",new(){["@uid"]=uid});
        var t=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N");
        await DbHelper.ExecuteNonQueryAsync(wc,"INSERT INTO email_verification_tokens (id,user_id,token,expires_at,created_at) VALUES (uuid_generate_v4(),@uid,@t,NOW()+INTERVAL '24 hours',NOW())",
            new(){["@uid"]=uid,["@t"]=t});
        try{await _emailService.SendEmailVerificationAsync(email,uname,t);}catch{}
        return true;
    }

    private async Task SaveRefreshTokenAsync(Guid userId, string refreshToken, string ipAddress, string? userAgent=null)
    {
        var th=_jwtService.HashToken(refreshToken);
        await using var c=await _db.CreateConnectionAsync();
        using var cmd=new NpgsqlCommand(@"
            INSERT INTO user_sessions (id,user_id,refresh_token_hash,ip_address,user_agent,is_active,last_used_at,expires_at,created_at)
            VALUES (uuid_generate_v4(),@uid,@h,@ip,@ua,TRUE,NOW(),NOW()+INTERVAL '30 days',NOW())",c);
        cmd.Parameters.AddWithValue("@uid",userId);
        cmd.Parameters.AddWithValue("@h",th);
        cmd.Parameters.Add(new NpgsqlParameter("@ip",NpgsqlTypes.NpgsqlDbType.Inet){
            Value=System.Net.IPAddress.TryParse(ipAddress,out var ip)?ip:System.Net.IPAddress.Loopback});
        cmd.Parameters.AddWithValue("@ua",(object?)userAgent??DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task LogLoginAsync(Guid? userId, string ipAddress, string method, bool success, string? failReason=null)
    {
        try
        {
            await using var c=await _db.CreateConnectionAsync();
            using var cmd=new NpgsqlCommand(@"
                INSERT INTO login_history (id,user_id,ip_address,login_method,is_successful,failure_reason,created_at)
                VALUES (uuid_generate_v4(),@uid,@ip,@m,@s,@r,NOW())",c);
            cmd.Parameters.AddWithValue("@uid",(object?)userId??DBNull.Value);
            cmd.Parameters.Add(new NpgsqlParameter("@ip",NpgsqlTypes.NpgsqlDbType.Inet){
                Value=System.Net.IPAddress.TryParse(ipAddress,out var ip)?ip:System.Net.IPAddress.Loopback});
            cmd.Parameters.AddWithValue("@m",method);
            cmd.Parameters.AddWithValue("@s",success);
            cmd.Parameters.AddWithValue("@r",(object?)failReason??DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        catch{}
    }

    private string GenerateReferralCode(string username)
    {
        var rand=new Random();
        return (username.ToUpper()[..Math.Min(4,username.Length)]+rand.Next(1000,9999)).ToUpper();
    }

    private async Task<string> GenerateUniqueUsernameAsync(NpgsqlConnection conn, string base_)
    {
        var u=base_; int count=0;
        while (true)
        {
            var e=await DbHelper.ExecuteScalarAsync<int>(conn,"SELECT COUNT(1) FROM users WHERE LOWER(username)=LOWER(@u)",new(){["@u"]=u});
            if (e==0) return u;
            u=base_+new Random().Next(10,999);
            if (++count>10) u=base_+Guid.NewGuid().ToString("N")[..4];
            if (count>15) break;
        }
        return u;
    }

    private bool Verify2FA(string? secret, string code)
    {
        if (string.IsNullOrEmpty(secret)) return false;
        try { var t=new Totp(Base32Encoding.ToBytes(secret)); return t.VerifyTotp(code,out _,VerificationWindow.RfcSpecifiedNetworkDelay); }
        catch { return false; }
    }
}