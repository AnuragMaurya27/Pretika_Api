using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Modules.Auth.Models;
using HauntedVoiceUniverse.Modules.Auth.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HauntedVoiceUniverse.Modules.Auth.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    // ─── POST /api/auth/register ──────────────────────────────────────────────
    /// <summary>Naya account banao</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), 201)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<object>.Fail(
                ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList()));

        var ip = GetIpAddress();
        var (success, message, _) = await _authService.RegisterAsync(req, ip);

        if (!success) return BadRequest(ApiResponse<AuthResponse>.Fail(message));
        // BUG#M1-2 FIX: RegisterAsync no longer returns tokens — email must be verified first.
        // Return 201 with just a message; no AuthResponse data.
        return StatusCode(201, ApiResponse.OkNoData(message));
    }

    // ─── POST /api/auth/login ─────────────────────────────────────────────────
    /// <summary>Email/username + password se login</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<object>.Fail("Invalid input"));

        var ip = GetIpAddress();
        var ua = Request.Headers["User-Agent"].ToString();
        var (success, message, data) = await _authService.LoginAsync(req, ip, ua);

        if (!success) return Unauthorized(ApiResponse<AuthResponse>.Fail(message, 401));
        return Ok(ApiResponse<AuthResponse>.Ok(data!, message));
    }

    // ─── POST /api/auth/google ────────────────────────────────────────────────
    /// <summary>Google OAuth login</summary>
    [HttpPost("google")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), 200)]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest req)
    {
        var (success, message, data) = await _authService.GoogleLoginAsync(req, GetIpAddress());
        if (!success) return BadRequest(ApiResponse<AuthResponse>.Fail(message));
        return Ok(ApiResponse<AuthResponse>.Ok(data!, message));
    }

    // ─── POST /api/auth/verify-email ──────────────────────────────────────────
    /// <summary>Email verification token se verify karo</summary>
    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest req)
    {
        var (success, message) = await _authService.VerifyEmailAsync(req.Token);
        if (!success) return BadRequest(ApiResponse.Fail(message));
        return Ok(ApiResponse.OkNoData(message));
    }

    // ─── POST /api/auth/resend-verification ───────────────────────────────────
    /// <summary>Verification email dobara bhejo</summary>
    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification([FromBody] ForgotPasswordRequest req)
    {
        await _authService.ResendVerificationEmailAsync(req.Email);
        return Ok(ApiResponse.OkNoData("Agar email registered hai toh verification mail bhej diya"));
    }

    // ─── POST /api/auth/forgot-password ──────────────────────────────────────
    /// <summary>Password reset email bhejo</summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
    {
        var (_, message) = await _authService.ForgotPasswordAsync(req.Email);
        return Ok(ApiResponse.OkNoData(message));
    }

    // ─── POST /api/auth/reset-password ───────────────────────────────────────
    /// <summary>Token se naya password set karo</summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse.Fail("Invalid input"));

        var (success, message) = await _authService.ResetPasswordAsync(req);
        if (!success) return BadRequest(ApiResponse.Fail(message));
        return Ok(ApiResponse.OkNoData(message));
    }

    // ─── POST /api/auth/change-password ──────────────────────────────────────
    /// <summary>Logged in user apna password change kare</summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var userId = GetUserId();
        var (success, message) = await _authService.ChangePasswordAsync(userId, req);
        if (!success) return BadRequest(ApiResponse.Fail(message));
        return Ok(ApiResponse.OkNoData(message));
    }

    // ─── POST /api/auth/refresh ───────────────────────────────────────────────
    /// <summary>Access token refresh karo using refresh token</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest req)
    {
        var (success, message, data) = await _authService.RefreshTokenAsync(req.RefreshToken, GetIpAddress());
        if (!success) return Unauthorized(ApiResponse<AuthResponse>.Fail(message, 401));
        return Ok(ApiResponse<AuthResponse>.Ok(data!, "Token refreshed"));
    }

    // ─── POST /api/auth/logout ────────────────────────────────────────────────
    /// <summary>Logout - refresh token invalidate ho jaayega</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest req)
    {
        var userId = GetUserId();
        await _authService.LogoutAsync(userId, req.RefreshToken);
        return Ok(ApiResponse.OkNoData("Logout successful"));
    }

    // ─── POST /api/auth/2fa/setup ─────────────────────────────────────────────
    /// <summary>2FA setup karo - QR code milega</summary>
    [HttpPost("2fa/setup")]
    [Authorize]
    public async Task<IActionResult> Setup2FA()
    {
        var userId = GetUserId();
        var (success, message, data) = await _authService.Setup2FAAsync(userId);
        if (!success) return BadRequest(ApiResponse<TwoFASetupResponse>.Fail(message));
        return Ok(ApiResponse<TwoFASetupResponse>.Ok(data!, message));
    }

    // ─── POST /api/auth/2fa/enable ────────────────────────────────────────────
    /// <summary>OTP verify karke 2FA enable karo</summary>
    [HttpPost("2fa/enable")]
    [Authorize]
    public async Task<IActionResult> Enable2FA([FromBody] Enable2FARequest req)
    {
        var userId = GetUserId();
        var (success, message) = await _authService.Enable2FAAsync(userId, req.OtpCode);
        if (!success) return BadRequest(ApiResponse.Fail(message));
        return Ok(ApiResponse.OkNoData(message));
    }

    // ─── POST /api/auth/2fa/disable ───────────────────────────────────────────
    /// <summary>OTP verify karke 2FA disable karo</summary>
    [HttpPost("2fa/disable")]
    [Authorize]
    public async Task<IActionResult> Disable2FA([FromBody] Enable2FARequest req)
    {
        var userId = GetUserId();
        var (success, message) = await _authService.Disable2FAAsync(userId, req.OtpCode);
        if (!success) return BadRequest(ApiResponse.Fail(message));
        return Ok(ApiResponse.OkNoData(message));
    }

    // ─── GET /api/auth/me ─────────────────────────────────────────────────────
    /// <summary>Current logged in user info (token se)</summary>
    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var claims = User.Claims.ToDictionary(c => c.Type, c => c.Value);
        return Ok(ApiResponse<Dictionary<string, string>>.Ok(claims, "Current user claims"));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private Guid GetUserId()
    {
        var uid = User.FindFirstValue(HauntedVoiceUniverse.Common.HvuClaims.UserId);
        return Guid.Parse(uid!);
    }

    private string GetIpAddress()
    {
        if (Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
            return forwardedFor.ToString().Split(',')[0].Trim();
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}