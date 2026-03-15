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
var keyBytes = Encoding.UTF8.GetBytes(secretKey);

builder.Services.AddAuthentication(opt =>
{
    opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    opt.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(opt =>
{
    opt.RequireHttpsMetadata = false;
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
builder.Services.AddCors(opt =>
{
    opt.AddDefaultPolicy(policy =>
    {
        // AllowAnyOrigin() can't be combined with AllowCredentials (needed for SignalR)
        // So we use SetIsOriginAllowed with AllowCredentials
        policy.SetIsOriginAllowed(_ => true)
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

// Creator (Dashboard Stats)
builder.Services.AddScoped<HauntedVoiceUniverse.Modules.Creator.Services.ICreatorService,
                             HauntedVoiceUniverse.Modules.Creator.Services.CreatorService>();

// Search + Reports
builder.Services.AddScoped<HauntedVoiceUniverse.Modules.Search.Services.ISearchService,
                             HauntedVoiceUniverse.Modules.Search.Services.SearchService>();

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ─── Middleware Pipeline ──────────────────────────────────────────────────────

// ✅ Swagger - hamesha ON (development + production dono mein)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "HVU API v1");
    c.RoutePrefix = string.Empty; // ✅ Root URL pe milega: http://localhost:5182
    c.DocumentTitle = "👻 Haunted Voice Universe API";
    c.DefaultModelsExpandDepth(-1); // Models section collapse rahega
});

app.UseIpRateLimiting();
app.UseCors();
app.UseStaticFiles(); // ✅ Avatar/Cover images serve karega: /uploads/avatars/...
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

app.Run();