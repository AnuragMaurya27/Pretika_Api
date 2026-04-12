namespace HauntedVoiceUniverse.Modules.Subscriptions.Models;

// ─── Plans ────────────────────────────────────────────────────────────────────

public class SubscriptionPlan
{
    public string Id { get; set; } = "";          // monthly | quarterly | biannual
    public string Name { get; set; } = "";
    public int DurationDays { get; set; }
    public decimal PriceInr { get; set; }
    public int PriceInCoins { get; set; }         // 1 INR = 10 coins
    public string Description { get; set; } = "";
    public bool IsPopular { get; set; }
}

// ─── User Subscription ────────────────────────────────────────────────────────

public class UserSubscriptionResponse
{
    public Guid Id { get; set; }
    public string PlanId { get; set; } = "";
    public string PlanName { get; set; } = "";
    public DateTime StartsAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public string PaymentMethod { get; set; } = ""; // coins | razorpay
    public int CoinsSpent { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ─── Purchase Request ─────────────────────────────────────────────────────────

public class PurchaseSubscriptionRequest
{
    public string PlanId { get; set; } = "";        // monthly | quarterly | biannual
    public string PaymentMethod { get; set; } = "coins"; // coins only for now
}

// ─── Active Status ────────────────────────────────────────────────────────────

public class SubscriptionStatusResponse
{
    public bool IsPremium { get; set; }
    public UserSubscriptionResponse? ActiveSubscription { get; set; }
}
