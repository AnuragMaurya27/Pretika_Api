using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace HauntedVoiceUniverse.Modules.Auth.Services;

public interface IEmailService
{
    Task SendEmailVerificationAsync(string toEmail, string username, string token);
    Task SendPasswordResetAsync(string toEmail, string username, string token);
    Task SendWelcomeEmailAsync(string toEmail, string username);
}

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendEmailVerificationAsync(string toEmail, string username, string token)
    {
        var verifyUrl = $"https://hauntedvoice.in/verify-email?token={token}";
        var body = $@"
        <div style='background:#0a0a0a;color:#fff;padding:40px;font-family:sans-serif;'>
            <h1 style='color:#dc143c;'>👻 The Haunted Voice Universe</h1>
            <h2>Namaste {username}! Email Verify Karo</h2>
            <p>Tera account almost ready hai. Sirf ek kaam bacha hai:</p>
            <a href='{verifyUrl}' 
               style='background:#dc143c;color:#fff;padding:12px 24px;
                      text-decoration:none;border-radius:4px;display:inline-block;margin:20px 0;'>
                ✅ Email Verify Karo
            </a>
            <p style='color:#888;font-size:12px;'>Ye link 24 ghante valid hai.</p>
            <p style='color:#555;font-size:11px;'>Agar tune register nahi kiya toh ignore karo.</p>
        </div>";

        await SendAsync(toEmail, "👻 Email Verify Karo - Haunted Voice Universe", body);
    }

    public async Task SendPasswordResetAsync(string toEmail, string username, string token)
    {
        var resetUrl = $"https://hauntedvoice.in/reset-password?token={token}";
        var body = $@"
        <div style='background:#0a0a0a;color:#fff;padding:40px;font-family:sans-serif;'>
            <h1 style='color:#dc143c;'>👻 The Haunted Voice Universe</h1>
            <h2>Password Reset - {username}</h2>
            <p>Tune password reset request kiya tha:</p>
            <a href='{resetUrl}' 
               style='background:#dc143c;color:#fff;padding:12px 24px;
                      text-decoration:none;border-radius:4px;display:inline-block;margin:20px 0;'>
                🔐 New Password Set Karo
            </a>
            <p style='color:#888;font-size:12px;'>Ye link 1 ghante tak valid hai.</p>
            <p style='color:#555;font-size:11px;'>Agar tune request nahi kiya toh apna account secure karo.</p>
        </div>";

        await SendAsync(toEmail, "🔐 Password Reset - Haunted Voice Universe", body);
    }

    public async Task SendWelcomeEmailAsync(string toEmail, string username)
    {
        var body = $@"
        <div style='background:#0a0a0a;color:#fff;padding:40px;font-family:sans-serif;'>
            <h1 style='color:#dc143c;'>👻 The Haunted Voice Universe</h1>
            <h2>Welcome {username}! Haunted World Mein Swagat Hai 🕸️</h2>
            <p>Tu ab India ke sabse bade horror storytelling platform ka hissa hai!</p>
            <p>Aaj se tu horror stories padh sakta hai, creators ko follow kar sakta hai, 
               aur apni khud ki kahaniyan likh sakta hai.</p>
            <a href='https://hauntedvoice.in' 
               style='background:#dc143c;color:#fff;padding:12px 24px;
                      text-decoration:none;border-radius:4px;display:inline-block;margin:20px 0;'>
                🚀 Explore Karo
            </a>
            <p style='color:#888;font-size:12px;'>Darr mat... ya phir darr! 😈</p>
        </div>";

        await SendAsync(toEmail, "🕸️ Haunted Voice Universe Mein Welcome!", body);
    }

    // ─── Private Send Method ─────────────────────────────────────────────────

    private async Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        try
        {
            var email = new MimeMessage();
            email.From.Add(new MailboxAddress(
                _config["Email:SenderName"] ?? "Haunted Voice",
                _config["Email:SenderEmail"]));
            email.To.Add(MailboxAddress.Parse(toEmail));
            email.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            email.Body = bodyBuilder.ToMessageBody();

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(
                _config["Email:SmtpHost"],
                int.Parse(_config["Email:SmtpPort"] ?? "587"),
                SecureSocketOptions.StartTls);

            await smtp.AuthenticateAsync(
                _config["Email:Username"],
                _config["Email:Password"]);

            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email send failed to {Email}", toEmail);
            // Don't throw - email failure shouldn't block registration
        }
    }
}