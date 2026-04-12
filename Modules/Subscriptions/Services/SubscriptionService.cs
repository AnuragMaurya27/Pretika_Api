using HauntedVoiceUniverse.Infrastructure.Database;
using HauntedVoiceUniverse.Modules.Subscriptions.Models;

namespace HauntedVoiceUniverse.Modules.Subscriptions.Services;

public interface ISubscriptionService
{
    List<SubscriptionPlan> GetPlans();
    Task<SubscriptionStatusResponse> GetStatusAsync(Guid userId);
    Task<(bool Success, string Message, UserSubscriptionResponse? Data)> PurchaseAsync(Guid userId, PurchaseSubscriptionRequest req);
    Task<bool> IsUserPremiumAsync(Guid userId);
    /// <summary>Called when a premium user reads a premium episode. Credits creator from platform.</summary>
    Task CreditCreatorForPremiumReadAsync(Guid creatorId, Guid episodeId, int episodeCoinCost);
}

public class SubscriptionService : ISubscriptionService
{
    private readonly IDbConnectionFactory _db;

    // Platform wallet user ID — a virtual system account that holds subscription revenue
    // In production this would be a real Guid from DB; for now we use a constant
    private static readonly Guid PlatformUserId = new("00000000-0000-0000-0000-000000000001");

    // 1 INR = 10 coins
    private const int CoinsPerInr = 10;

    // Creator payout per premium read = 70% of episode's coin cost
    // (Platform keeps 30% for operations)
    private const double CreatorPremiumSharePct = 0.70;

    private static readonly List<SubscriptionPlan> _plans = new()
    {
        new SubscriptionPlan
        {
            Id           = "monthly",
            Name         = "Monthly",
            DurationDays = 30,
            PriceInr     = 39,
            PriceInCoins = 390,   // 39 × 10
            Description  = "Unlimited access for 1 month",
            IsPopular    = false,
        },
        new SubscriptionPlan
        {
            Id           = "quarterly",
            Name         = "Quarterly",
            DurationDays = 90,
            PriceInr     = 100,
            PriceInCoins = 1000,  // 100 × 10
            Description  = "Unlimited access for 3 months",
            IsPopular    = true,
        },
        new SubscriptionPlan
        {
            Id           = "biannual",
            Name         = "6 Months",
            DurationDays = 180,
            PriceInr     = 180,
            PriceInCoins = 1800,  // 180 × 10
            Description  = "Unlimited access for 6 months",
            IsPopular    = false,
        },
    };

    public SubscriptionService(IDbConnectionFactory db)
    {
        _db = db;
    }

    public List<SubscriptionPlan> GetPlans() => _plans;

    public async Task<bool> IsUserPremiumAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        // Ensure table exists
        await EnsureTableAsync(conn);
        return await DbHelper.ExecuteScalarAsync<bool>(conn,
            "SELECT EXISTS(SELECT 1 FROM user_subscriptions WHERE user_id=@uid AND expires_at > NOW() AND status='active')",
            new Dictionary<string, object?> { ["@uid"] = userId });
    }

    public async Task<SubscriptionStatusResponse> GetStatusAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        await EnsureTableAsync(conn);

        var sub = await DbHelper.ExecuteReaderFirstAsync(conn,
            @"SELECT id, plan_id, plan_name, starts_at, expires_at, payment_method, coins_spent, created_at
              FROM user_subscriptions
              WHERE user_id=@uid AND status='active'
              ORDER BY expires_at DESC LIMIT 1",
            r => new UserSubscriptionResponse
            {
                Id            = DbHelper.GetGuid(r, "id"),
                PlanId        = DbHelper.GetString(r, "plan_id"),
                PlanName      = DbHelper.GetString(r, "plan_name"),
                StartsAt      = DbHelper.GetDateTime(r, "starts_at"),
                ExpiresAt     = DbHelper.GetDateTime(r, "expires_at"),
                IsActive      = true,
                PaymentMethod = DbHelper.GetString(r, "payment_method"),
                CoinsSpent    = DbHelper.GetInt(r, "coins_spent"),
                CreatedAt     = DbHelper.GetDateTime(r, "created_at"),
            },
            new Dictionary<string, object?> { ["@uid"] = userId });

        if (sub != null && sub.ExpiresAt <= DateTime.UtcNow)
        {
            // Expired — mark as expired
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE user_subscriptions SET status='expired' WHERE user_id=@uid AND status='active' AND expires_at <= NOW()",
                new Dictionary<string, object?> { ["@uid"] = userId });
            sub = null;
        }

        return new SubscriptionStatusResponse
        {
            IsPremium = sub != null,
            ActiveSubscription = sub
        };
    }

    public async Task<(bool Success, string Message, UserSubscriptionResponse? Data)> PurchaseAsync(
        Guid userId, PurchaseSubscriptionRequest req)
    {
        var plan = _plans.Find(p => p.Id == req.PlanId);
        if (plan == null) return (false, "Invalid plan", null);

        using var conn = await _db.CreateConnectionAsync();
        await EnsureTableAsync(conn);

        // Check for existing active subscription
        var hasActive = await DbHelper.ExecuteScalarAsync<bool>(conn,
            "SELECT EXISTS(SELECT 1 FROM user_subscriptions WHERE user_id=@uid AND expires_at > NOW() AND status='active')",
            new Dictionary<string, object?> { ["@uid"] = userId });

        if (hasActive)
            return (false, "Aapka premium subscription pehle se active hai!", null);

        if (req.PaymentMethod == "coins")
        {
            // Check coin balance
            var balance = await DbHelper.ExecuteScalarAsync<long>(conn,
                "SELECT COALESCE(coin_balance, 0) FROM wallets WHERE user_id=@uid",
                new Dictionary<string, object?> { ["@uid"] = userId });

            if (balance < plan.PriceInCoins)
                return (false, $"Insufficient coins. {plan.PriceInCoins} coins chahiye, aapke paas {balance} hain.", null);

            await using var tx = await conn.BeginTransactionAsync();
            bool committed = false;
            try
            {
                // Deduct coins from user
                await DbHelper.ExecuteNonQueryAsync(conn,
                    "UPDATE wallets SET coin_balance=coin_balance-@cost, total_spent=total_spent+@cost, updated_at=NOW() WHERE user_id=@uid",
                    new Dictionary<string, object?> { ["@uid"] = userId, ["@cost"] = plan.PriceInCoins }, tx);

                // Log transaction
                await DbHelper.ExecuteNonQueryAsync(conn,
                    @"INSERT INTO coin_transactions (id, sender_id, transaction_type, amount, description, status, created_at)
                      VALUES (uuid_generate_v4(), @uid, 'subscription'::transaction_type, @cost, @desc, 'completed'::transaction_status, NOW())",
                    new Dictionary<string, object?> { ["@uid"] = userId, ["@cost"] = plan.PriceInCoins, ["@desc"] = $"Premium subscription: {plan.Name}" }, tx);

                // Create subscription record
                var subId = Guid.NewGuid();
                var now = DateTime.UtcNow;
                var expires = now.AddDays(plan.DurationDays);
                await DbHelper.ExecuteNonQueryAsync(conn,
                    @"INSERT INTO user_subscriptions (id, user_id, plan_id, plan_name, starts_at, expires_at, payment_method, coins_spent, status, created_at)
                      VALUES (@id, @uid, @planId, @planName, @starts, @expires, 'coins', @cost, 'active', NOW())",
                    new Dictionary<string, object?>
                    {
                        ["@id"] = subId, ["@uid"] = userId, ["@planId"] = plan.Id,
                        ["@planName"] = plan.Name, ["@starts"] = now, ["@expires"] = expires,
                        ["@cost"] = plan.PriceInCoins
                    }, tx);

                await tx.CommitAsync();
                committed = true;

                var result = new UserSubscriptionResponse
                {
                    Id = subId, PlanId = plan.Id, PlanName = plan.Name,
                    StartsAt = now, ExpiresAt = expires, IsActive = true,
                    PaymentMethod = "coins", CoinsSpent = plan.PriceInCoins, CreatedAt = now
                };
                return (true, $"Premium subscription activated! {plan.Name} plan 🎉", result);
            }
            catch
            {
                if (!committed) try { await tx.RollbackAsync(); } catch { }
                return (false, "Purchase failed. Please try again.", null);
            }
        }

        return (false, "Payment method not supported", null);
    }

    /// <summary>
    /// When a premium user reads a premium episode, we auto-unlock it for them and
    /// credit the creator 70% of the episode coin cost from the platform.
    /// Logic: Premium subscription pool = subscription revenue (coins collected).
    /// Platform pays creator from this pool to ensure creators aren't disadvantaged.
    /// </summary>
    public async Task CreditCreatorForPremiumReadAsync(Guid creatorId, Guid episodeId, int episodeCoinCost)
    {
        if (episodeCoinCost <= 0) return;
        var creatorShare = Math.Max(1, (int)(episodeCoinCost * CreatorPremiumSharePct));

        try
        {
            using var conn = await _db.CreateConnectionAsync();

            // Credit creator wallet
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE wallets SET coin_balance=coin_balance+@coins, total_earned=total_earned+@coins, updated_at=NOW() WHERE user_id=@uid",
                new Dictionary<string, object?> { ["@uid"] = creatorId, ["@coins"] = creatorShare });

            // Log creator earning
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO coin_transactions (id, receiver_id, transaction_type, amount, description, status, created_at)
                  VALUES (uuid_generate_v4(), @uid, 'premium_read'::transaction_type, @coins, 'Premium reader episode unlock', 'completed'::transaction_status, NOW())",
                new Dictionary<string, object?> { ["@uid"] = creatorId, ["@coins"] = creatorShare });
        }
        catch
        {
            // Non-critical — don't fail the read request
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static async Task EnsureTableAsync(Npgsql.NpgsqlConnection conn)
    {
        await DbHelper.ExecuteNonQueryAsync(conn,
            @"CREATE TABLE IF NOT EXISTS user_subscriptions (
                id           UUID PRIMARY KEY,
                user_id      UUID NOT NULL,
                plan_id      TEXT NOT NULL,
                plan_name    TEXT NOT NULL,
                starts_at    TIMESTAMPTZ NOT NULL,
                expires_at   TIMESTAMPTZ NOT NULL,
                payment_method TEXT NOT NULL DEFAULT 'coins',
                coins_spent  INT NOT NULL DEFAULT 0,
                status       TEXT NOT NULL DEFAULT 'active',
                created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )");
    }
}
