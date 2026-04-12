using System.ComponentModel.DataAnnotations;

namespace HauntedVoiceUniverse.Modules.Wallet.Models;

public class WalletResponse
{
    public Guid Id { get; set; }
    public long CoinBalance { get; set; }
    public long TotalEarned { get; set; }
    public long TotalSpent { get; set; }
    public long TotalWithdrawn { get; set; }
    public long PendingWithdrawal { get; set; }
    public bool IsFrozen { get; set; }
    public string? FrozenReason { get; set; }
    public DateTime? LastTransactionAt { get; set; }
    // Computed
    public decimal BalanceInr { get; set; }
}

public class TransactionResponse
{
    public Guid Id { get; set; }
    public Guid? SenderId { get; set; }
    public string? SenderUsername { get; set; }
    public Guid? ReceiverId { get; set; }
    public string? ReceiverUsername { get; set; }
    public string TransactionType { get; set; } = "";
    public string Status { get; set; } = "";
    public long Amount { get; set; }
    public string? Description { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public decimal? AmountInr { get; set; }
    public bool IsFlagged { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class RechargePackResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public decimal AmountInr { get; set; }
    public int Coins { get; set; }
    public int BonusCoins { get; set; }
    public decimal BonusPercentage { get; set; }
    public int TotalCoins { get; set; }  // coins + bonus_coins
    public bool IsPopular { get; set; }
    public int DisplayOrder { get; set; }
}

public class AppreciateRequest
{
    [Required]
    public Guid ReceiverId { get; set; }
    public Guid? StoryId { get; set; }
    public Guid? EpisodeId { get; set; }

    [Required]
    [Range(1, 10000)]
    public int CoinAmount { get; set; }

    [StringLength(200)]
    public string? Message { get; set; }
}

public class AppreciationResponse
{
    public Guid Id { get; set; }
    public Guid ReceiverId { get; set; }
    public string ReceiverUsername { get; set; } = "";
    public int CoinAmount { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class WithdrawalRequest
{
    [Required]
    [Range(1000, long.MaxValue, ErrorMessage = "Minimum 1000 coins chahiye")]
    public long CoinAmount { get; set; }

    [Required]
    public string PaymentMethod { get; set; } = ""; // 'upi' or 'bank_transfer'

    // UPI
    public string? UpiId { get; set; }

    // Bank
    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankIfsc { get; set; }
    public string? AccountHolderName { get; set; }
}

public class WithdrawalResponse
{
    public Guid Id { get; set; }
    public long CoinAmount { get; set; }
    public decimal InrAmount { get; set; }
    public decimal GstAmount { get; set; }
    public decimal TdsAmount { get; set; }
    public decimal NetAmount { get; set; }
    public string Status { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public string? UpiId { get; set; }
    public string? BankName { get; set; }
    public string? AccountHolderName { get; set; }
    public string? RejectionReason { get; set; }
    public string? TransactionReference { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

public class InitiateRechargeRequest
{
    [Required]
    public Guid PackId { get; set; }

    [Required]
    public string PaymentGateway { get; set; } = "razorpay";  // razorpay, paytm, phonepe
}

public class InitiateRechargeResponse
{
    public Guid TransactionId { get; set; }
    public string OrderId { get; set; } = "";   // Gateway order ID
    public decimal AmountInr { get; set; }
    public int Coins { get; set; }
    public int BonusCoins { get; set; }
    public string PaymentGateway { get; set; } = "";
}

public class VerifyRechargeRequest
{
    [Required]
    public Guid TransactionId { get; set; }

    [Required]
    public string GatewayTransactionId { get; set; } = "";

    // BUG#1 FIX: Required for Razorpay — HMAC-SHA256(order_id|payment_id, key_secret)
    [Required]
    public string GatewaySignature { get; set; } = "";
}
