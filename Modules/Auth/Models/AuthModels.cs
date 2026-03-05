using System.ComponentModel.DataAnnotations;

namespace HauntedVoiceUniverse.Modules.Auth.Models;

// ─── Request DTOs ─────────────────────────────────────────────────────────────

public class RegisterRequest
{
    [Required, MinLength(3), MaxLength(50)]
    [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "Only letters, numbers, underscore allowed")]
    public string Username { get; set; } = "";

    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, MinLength(8)]
    public string Password { get; set; } = "";

    [MaxLength(100)]
    public string? DisplayName { get; set; }

    public string? ReferralCode { get; set; }

    public string? PreferredLanguage { get; set; } = "hindi"; // hindi/hinglish/english
}

public class LoginRequest
{
    [Required]
    public string EmailOrUsername { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";

    public string? TwoFaCode { get; set; }

    public string? DeviceInfo { get; set; } // JSON string
}

public class GoogleLoginRequest
{
    [Required]
    public string IdToken { get; set; } = "";
}

public class ForgotPasswordRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";
}

public class ResetPasswordRequest
{
    [Required]
    public string Token { get; set; } = "";

    [Required, MinLength(8)]
    public string NewPassword { get; set; } = "";
}

public class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = "";

    [Required, MinLength(8)]
    public string NewPassword { get; set; } = "";
}

public class RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; set; } = "";
}

public class VerifyEmailRequest
{
    [Required]
    public string Token { get; set; } = "";
}

public class Enable2FARequest
{
    [Required, StringLength(6, MinimumLength = 6)]
    public string OtpCode { get; set; } = "";
}

// ─── Response DTOs ────────────────────────────────────────────────────────────

public class AuthResponse
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime AccessTokenExpiry { get; set; }
    public DateTime RefreshTokenExpiry { get; set; }
    public UserAuthInfo User { get; set; } = new();
}

public class UserAuthInfo
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string Role { get; set; } = "";
    public bool IsCreator { get; set; }
    public bool IsEmailVerified { get; set; }
    public bool Is2FAEnabled { get; set; }
    public string ReaderFearRank { get; set; } = "";
    public string? CreatorFearRank { get; set; }
    public long CoinBalance { get; set; }
}

public class TwoFASetupResponse
{
    public string SecretKey { get; set; } = "";
    public string QrCodeUrl { get; set; } = "";
    public string ManualEntryCode { get; set; } = "";
}

public class TokenInfo
{
    public Guid UserId { get; set; }
    public string Role { get; set; } = "";
    public string Email { get; set; } = "";
    public string Username { get; set; } = "";
    public bool IsCreator { get; set; }
}