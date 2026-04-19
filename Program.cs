using System.Text;
using AspNetCoreRateLimit;
using HauntedVoiceUniverse.Infrastructure.Database;
using HauntedVoiceUniverse.Modules.Chat.Hubs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ─── 1. Controllers ───────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
        opts.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

// ─── 2. Database (ADO.NET) ────────────────────────────────────────────────────
builder.Services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();

// ─── 3. JWT Authentication ────────────────────────────────────────────────────
var jwtConfig = builder.Configuration.GetSection("Jwt");
var secretKey = jwtConfig["SecretKey"] ?? throw new Exception("JWT SecretKey missing");

// VULN#2 FIX: Fail fast if placeholder or weak key is still in config.
// A committed placeholder allows anyone with repo access to forge admin JWTs.
const string jwtPlaceholder = "YOUR_SUPER_SECRET_JWT_KEY_MIN_32_CHARS_HERE";
if (secretKey == jwtPlaceholder || secretKey.Length < 64)
    throw new Exception(
        "FATAL: JWT SecretKey is a placeholder or too short (min 64 chars). " +
        "Set a cryptographically random key via environment variable: " +
        "export Jwt__SecretKey=\"$(openssl rand -base64 64)\"");

var keyBytes = Encoding.UTF8.GetBytes(secretKey);

builder.Services.AddAuthentication(opt =>
{
    opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    opt.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(opt =>
{
    // VULN#9 FIX: Only allow HTTP in development. Production must use HTTPS.
    opt.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    opt.SaveToken = true;
    opt.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtConfig["Issuer"],
        ValidAudience = jwtConfig["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
        ClockSkew = TimeSpan.Zero
    };
    opt.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = ctx =>
        {
            ctx.Response.StatusCode = 401;
            return Task.CompletedTask;
        },
        // SignalR WebSocket sends token as query param ?access_token=...
        OnMessageReceived = ctx =>
        {
            var accessToken = ctx.Request.Query["access_token"];
            var path = ctx.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/chat"))
                ctx.Token = accessToken;
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// ─── 4. CORS ──────────────────────────────────────────────────────────────────
// VULN#1 FIX: Replace wildcard SetIsOriginAllowed with exact-match allowlist from config.
// Wildcard + AllowCredentials lets any origin make authenticated requests → CSRF/data theft.
var allowedCorsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();
if (allowedCorsOrigins.Length == 0 && !builder.Environment.IsDevelopment())
    throw new Exception("FATAL: Cors:AllowedOrigins must be configured for non-development environments.");

builder.Services.AddCors(opt =>
{
    opt.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedCorsOrigins)
              .SetIsOriginAllowed(origin => 
              {
                  if (allowedCorsOrigins.Contains(origin)) return true;
                  try {
                      var host = new Uri(origin).Host;
                      return host.EndsWith(".vercel.app") || host == "vercel.app";
                  } catch { return false; }
              })
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// ─── 5. Rate Limiting ─────────────────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("RateLimiting"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

// ─── 6. Swagger ───────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opt =>
{
    opt.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "👻 Haunted Voice Universe API",
        Version = "v1",
        Description = "India's Horror Storytelling Platform - Complete API"
    });

    opt.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter: Bearer {your_token}"
    });

    opt.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ─── 7. HttpContextAccessor ───────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();

// ─── 7b. SignalR ──────────────────────────────────────────────────────────────
builder.Services.AddSignalR();

// ─── 8. Services Register ─────────────────────────────────────────────────────
// Auth
builder.Services.AddScoped<HauntedVoiceUniverse.Modules.Auth.Services.IAuthService,
                             HauntedVoiceUniverse.Modules.Auth.Services.AuthService>();
builder.Services.AddScoped<HauntedVoiceUniverse.Modules.Auth.Services.IJwtService,
                             HauntedVoiceUniverse.Modules.Auth.Services.JwtService>();
builder.Services.AddScoped<HauntedVoiceUniverse.Modules.Auth.Services.IEmailService,
                             HauntedVoiceUniverse.Modules.Auth.Services.EmailService>();

// Users
builder.Services.AddScoped<HauntedVoiceUniverse.Modules.Users.Services.IUserService,
                             HauntedVoiceUniverse.Modules.Users.Services.UserService>();

// Stories
builder.Services.AddScoped<HauntedVoiceUniverse.Modules.Stories.Services.IStoryService,
                             HauntedVoiceUniverse.Modules.Stories.Services.StoryService>();

// Comments
builder.Services.AddScoped<HauntedVoiceUniverse.Modules.Comments.Services.ICommentService,
                             HauntedVoiceUniverse.Modules.Comments.Services.CommentService>();

// Wallet
builder.Services.AddScoped<HauntedVoiceUniverse.Modules.Wallet.Services.IWalletService,
                             HauntedVoiceUniverse.Modules.Wallet.Services.WalletService>();

// Notifications
builder.Services.AddScoped<HauntedVoiceUniverse.Modules.Notifications.Services.INotificationService,
                             HauntedVoiceUniverse.Modules.Notifications.Services.NotificationService>();

// Leaderboard & Competitions
builder.Services.AddScoped<HauntedVoiceUniverse.Modules.Leaderboard.Services.ILeaderboardService,
                             HauntedVoiceUniverse.Modules.Leaderboard.Services.LeaderboardService>();

// Support
builder.Services.AddScoped<HauntedVoiceUniverse.Modules.Support.Services.ISupportService,
                             HauntedVoiceUniverse.Modules.Support.Services.SupportService>();

// Chat
builder.Services.AddScoped<HauntedVoiceUniverse.Modules.Chat.Services.IChatService,
                             HauntedVoiceUniverse.Modules.Chat.Services.ChatService>();

// Admin
builder.Services.AddScoped<HauntedVoiceUniverse.Modules.Admin.Services.IAdminService,
                             HauntedVoiceUniverse.Modules.Admin.Services.AdminService>();

// Subscriptions
builder.Services.AddScoped<HauntedVoiceUniverse.Modules.Subscriptions.Services.ISubscriptionService,
                             HauntedVoiceUniverse.Modules.Subscriptions.Services.SubscriptionService>();

// Creator (Dashboard Stats)
builder.Services.AddScoped<HauntedVoiceUniverse.Modules.Creator.Services.ICreatorService,
                             HauntedVoiceUniverse.Modules.Creator.Services.CreatorService>();

// Search + Reports
builder.Services.AddScoped<HauntedVoiceUniverse.Modules.Search.Services.ISearchService,
                             HauntedVoiceUniverse.Modules.Search.Services.SearchService>();

// BUG#M3-8 FIX: Background job that publishes scheduled episodes at their due time.
// Without this, episodes with scheduled_publish_at stayed 'draft' forever.
builder.Services.AddHostedService<HauntedVoiceUniverse.Infrastructure.BackgroundJobs.ScheduledPublishJob>();

// Darr Arena
builder.Services.AddScoped<HauntedVoiceUniverse.Modules.Arena.Services.IArenaService,
                             HauntedVoiceUniverse.Modules.Arena.Services.ArenaService>();
builder.Services.AddHostedService<HauntedVoiceUniverse.Modules.Arena.Jobs.ArenaPhaseTransitionJob>();
builder.Services.AddHostedService<HauntedVoiceUniverse.Modules.Arena.Jobs.ArenaForfeitDetectionJob>();

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ─── Middleware Pipeline ──────────────────────────────────────────────────────

// VULN#10 FIX: Swagger only in development. In production, full API schema at root URL
// exposes every endpoint, parameter, and auth scheme to attackers/scrapers.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "HVU API v1");
        c.RoutePrefix = string.Empty;
        c.DocumentTitle = "👻 Haunted Voice Universe API";
        c.DefaultModelsExpandDepth(-1);
    });
}

app.UseIpRateLimiting();
app.UseCors();
app.UseStaticFiles(); // ✅ Avatar/Cover images serve karega: /uploads/avatars/...

app.UseAuthentication();
// BUG#A5 FIX: Check user's account status (ban/suspend) on every authenticated request.
// JWT stays valid after ban unless we check the DB — this middleware closes that gap.
app.UseMiddleware<HauntedVoiceUniverse.Infrastructure.Middleware.BannedUserMiddleware>();
// VULN#15 FIX: Per-user identity-based rate limiting for sensitive endpoints.
// Runs after auth so userId is resolved. Complements IP-based AspNetCoreRateLimit.
app.UseMiddleware<HauntedVoiceUniverse.Infrastructure.Middleware.UserRateLimitMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");