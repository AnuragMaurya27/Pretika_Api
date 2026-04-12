using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Infrastructure.Database;
using HauntedVoiceUniverse.Modules.Wallet.Models;
using Npgsql;

namespace HauntedVoiceUniverse.Modules.Wallet.Services;

public interface IWalletService
{
    Task<WalletResponse?> GetWalletAsync(Guid userId);
    Task EnsureWalletExistsAsync(Guid userId);
    Task<PagedResult<TransactionResponse>> GetTransactionsAsync(Guid userId, int page, int pageSize, string? type);
    Task<List<RechargePackResponse>> GetRechargePacksAsync();
    Task<(bool Success, string Message, InitiateRechargeResponse? Data)> InitiateRechargeAsync(Guid userId, InitiateRechargeRequest req);
    Task<(bool Success, string Message)> VerifyRechargeAsync(Guid userId, VerifyRechargeRequest req);
    Task<(bool Success, string Message, AppreciationResponse? Data)> AppreciateAsync(Guid senderId, AppreciateRequest req);
    Task<(bool Success, string Message, WithdrawalResponse? Data)> RequestWithdrawalAsync(Guid userId, WithdrawalRequest req);
    Task<PagedResult<WithdrawalResponse>> GetWithdrawalHistoryAsync(Guid userId, int page, int pageSize);
}

public class WalletService : IWalletService
{
    private readonly IDbConnectionFactory _db;
    private readonly IConfiguration _config;

    // Coin to INR rate: 10 coins = ₹1 => 1 coin = ₹0.10
    private const decimal CoinToInrRate = 0.10m;
    private const decimal GstRate = 0.18m;   // 18% GST on platform fee
    private const decimal TdsRate = 0.10m;   // 10% TDS on creator earnings

    public WalletService(IDbConnectionFactory db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task EnsureWalletExistsAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        await DbHelper.ExecuteNonQueryAsync(conn,
            "INSERT INTO wallets (user_id) VALUES (@uid) ON CONFLICT (user_id) DO NOTHING",
            new Dictionary<string, object?> { ["uid"] = userId });
    }

    public async Task<WalletResponse?> GetWalletAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        await EnsureWalletExistsAsync(userId);

        return await DbHelper.ExecuteReaderFirstAsync(conn,
            "SELECT * FROM wallets WHERE user_id = @uid",
            r => new WalletResponse
            {
                Id = DbHelper.GetGuid(r, "id"),
                CoinBalance = DbHelper.GetLong(r, "coin_balance"),
                TotalEarned = DbHelper.GetLong(r, "total_earned"),
                TotalSpent = DbHelper.GetLong(r, "total_spent"),
                TotalWithdrawn = DbHelper.GetLong(r, "total_withdrawn"),
                PendingWithdrawal = DbHelper.GetLong(r, "pending_withdrawal"),
                IsFrozen = DbHelper.GetBool(r, "is_frozen"),
                FrozenReason = DbHelper.GetStringOrNull(r, "frozen_reason"),
                LastTransactionAt = DbHelper.GetDateTimeOrNull(r, "last_transaction_at"),
                BalanceInr = DbHelper.GetLong(r, "coin_balance") * CoinToInrRate
            },
            new Dictionary<string, object?> { ["uid"] = userId });
    }

    public async Task<PagedResult<TransactionResponse>> GetTransactionsAsync(Guid userId, int page, int pageSize, string? type)
    {
        using var conn = await _db.CreateConnectionAsync();
        pageSize = Math.Min(pageSize, 50);
        int offset = (page - 1) * pageSize;

        var whereParts = new List<string>
        {
            "(sender_id = @uid OR receiver_id = @uid)"
        };
        var parameters = new Dictionary<string, object?> { ["uid"] = userId };

        if (!string.IsNullOrEmpty(type))
        {
            whereParts.Add("transaction_type = @type::transaction_type");
            parameters["type"] = type;
        }

        var where = string.Join(" AND ", whereParts);
        var countSql = $"SELECT COUNT(*) FROM coin_transactions WHERE {where}";
        var total = await DbHelper.ExecuteScalarAsync<long>(conn, countSql, parameters);

        parameters["limit"] = pageSize;
        parameters["offset"] = offset;

        var sql = $@"
            SELECT t.*,
                   s.username as sender_username,
                   r.username as receiver_username
            FROM coin_transactions t
            LEFT JOIN users s ON s.id = t.sender_id
            LEFT JOIN users r ON r.id = t.receiver_id
            WHERE {where}
            ORDER BY t.created_at DESC
            LIMIT @limit OFFSET @offset";

        var items = await DbHelper.ExecuteReaderAsync(conn, sql, r => new TransactionResponse
        {
            Id = DbHelper.GetGuid(r, "id"),
            SenderId = r.IsDBNull(r.GetOrdinal("sender_id")) ? null : DbHelper.GetGuid(r, "sender_id"),
            SenderUsername = DbHelper.GetStringOrNull(r, "sender_username"),
            ReceiverId = r.IsDBNull(r.GetOrdinal("receiver_id")) ? null : DbHelper.GetGuid(r, "receiver_id"),
            ReceiverUsername = DbHelper.GetStringOrNull(r, "receiver_username"),
            TransactionType = DbHelper.GetString(r, "transaction_type"),
            Status = DbHelper.GetString(r, "status"),
            Amount = DbHelper.GetLong(r, "amount"),
            Description = DbHelper.GetStringOrNull(r, "description"),
            ReferenceType = DbHelper.GetStringOrNull(r, "reference_type"),
            ReferenceId = r.IsDBNull(r.GetOrdinal("reference_id")) ? null : DbHelper.GetGuid(r, "reference_id"),
            AmountInr = DbHelper.GetDecimal(r, "amount_inr"),
            IsFlagged = DbHelper.GetBool(r, "is_flagged"),
            CreatedAt = DbHelper.GetDateTime(r, "created_at"),
            CompletedAt = DbHelper.GetDateTimeOrNull(r, "completed_at")
        }, parameters);

        return PagedResult<TransactionResponse>.Create(items, (int)total, page, pageSize);
    }

    public async Task<List<RechargePackResponse>> GetRechargePacksAsync()
    {
        using var conn = await _db.CreateConnectionAsync();
        return await DbHelper.ExecuteReaderAsync(conn,
            "SELECT * FROM recharge_packs WHERE is_active = TRUE ORDER BY display_order, amount_inr",
            r => new RechargePackResponse
            {
                Id = DbHelper.GetGuid(r, "id"),
                Name = DbHelper.GetString(r, "name"),
                AmountInr = DbHelper.GetDecimal(r, "amount_inr"),
                Coins = DbHelper.GetInt(r, "coins"),
                BonusCoins = DbHelper.GetInt(r, "bonus_coins"),
                BonusPercentage = DbHelper.GetDecimal(r, "bonus_percentage"),
                TotalCoins = DbHelper.GetInt(r, "coins") + DbHelper.GetInt(r, "bonus_coins"),
                IsPopular = DbHelper.GetBool(r, "is_popular"),
                DisplayOrder = DbHelper.GetInt(r, "display_order")
            });
    }

    public async Task<(bool Success, string Message, InitiateRechargeResponse? Data)> InitiateRechargeAsync(Guid userId, InitiateRechargeRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();

        var pack = await DbHelper.ExecuteReaderFirstAsync(conn,
            "SELECT * FROM recharge_packs WHERE id = @id AND is_active = TRUE",
            r => new RechargePackResponse
            {
                Id = DbHelper.GetGuid(r, "id"),
                Name = DbHelper.GetString(r, "name"),
                AmountInr = DbHelper.GetDecimal(r, "amount_inr"),
                Coins = DbHelper.GetInt(r, "coins"),
                BonusCoins = DbHelper.GetInt(r, "bonus_coins"),
                TotalCoins = DbHelper.GetInt(r, "coins") + DbHelper.GetInt(r, "bonus_coins")
            },
            new Dictionary<string, object?> { ["id"] = req.PackId });

        if (pack == null) return (false, "Recharge pack nahi mila", null);

        // Create real Razorpay order
        string orderId;
        try
        {
            orderId = await CreateRazorpayOrderAsync(pack.AmountInr);
        }
        catch (Exception ex)
        {
            return (false, $"Payment gateway error: {ex.Message}", null);
        }

        var txId = Guid.NewGuid();

        await DbHelper.ExecuteNonQueryAsync(conn,
            @"INSERT INTO coin_transactions
                (id, receiver_id, transaction_type, status, amount, amount_inr,
                 payment_gateway, description, metadata)
              VALUES
                (@id, @uid, 'recharge', 'pending', @coins, @inr,
                 @gateway::payment_gateway, @desc, @meta::jsonb)",
            new Dictionary<string, object?>
            {
                ["id"] = txId,
                ["uid"] = userId,
                ["coins"] = pack.TotalCoins,
                ["inr"] = pack.AmountInr,
                ["gateway"] = req.PaymentGateway,
                ["desc"] = $"Recharge: {pack.Name} - {pack.TotalCoins} coins",
                ["meta"] = $"{{\"pack_id\":\"{pack.Id}\",\"order_id\":\"{orderId}\",\"pack_name\":\"{pack.Name}\"}}"
            });

        return (true, "Order create ho gaya", new InitiateRechargeResponse
        {
            TransactionId = txId,
            OrderId = orderId,
            AmountInr = pack.AmountInr,
            Coins = pack.Coins,
            BonusCoins = pack.BonusCoins,
            PaymentGateway = req.PaymentGateway
        });
    }

    // Call Razorpay Orders API to create a real order — returns "order_XXXX" ID
    private async Task<string> CreateRazorpayOrderAsync(decimal amountInr)
    {
        var keyId = _config["Razorpay:KeyId"] ?? throw new InvalidOperationException("Razorpay:KeyId missing");
        var keySecret = _config["Razorpay:KeySecret"] ?? throw new InvalidOperationException("Razorpay:KeySecret missing");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{keySecret}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var amountPaise = (long)(amountInr * 100);
        var body = JsonSerializer.Serialize(new
        {
            amount = amountPaise,
            currency = "INR",
            receipt = $"rcpt_{Guid.NewGuid().ToString()[..8]}"
        });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await http.PostAsync("https://api.razorpay.com/v1/orders", content);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Razorpay order creation failed: {json}");

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("id").GetString()
               ?? throw new Exception("Razorpay order ID missing in response");
    }

    // BUG#1 FIX: Verify Razorpay HMAC-SHA256 signature
    // Signature = HMAC_SHA256(order_id + "|" + razorpay_payment_id, key_secret)
    private bool VerifyRazorpaySignature(string orderId, string gatewayPaymentId, string signature)
    {
        var secret = _config["Razorpay:KeySecret"];
        if (string.IsNullOrEmpty(secret)) return false; // Config missing = deny
        var payload = $"{orderId}|{gatewayPaymentId}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        // Constant-time comparison to prevent timing attacks
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed.ToLower()),
            Encoding.UTF8.GetBytes(signature.ToLower()));
    }

    public async Task<(bool Success, string Message)> VerifyRechargeAsync(Guid userId, VerifyRechargeRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();
        using var transaction = await conn.BeginTransactionAsync();

        try
        {
            // Lock the transaction row — also fetches order_id stored in metadata
            var tx = await DbHelper.ExecuteReaderFirstAsync(conn,
                @"SELECT id, receiver_id, amount, status,
                         metadata->>'order_id' AS order_id,
                         payment_gateway
                  FROM coin_transactions WHERE id = @id FOR UPDATE",
                r => new
                {
                    Id         = DbHelper.GetGuid(r, "id"),
                    ReceiverId = DbHelper.GetGuid(r, "receiver_id"),
                    Amount     = DbHelper.GetLong(r, "amount"),
                    Status     = DbHelper.GetString(r, "status"),
                    OrderId    = DbHelper.GetStringOrNull(r, "order_id"),
                    Gateway    = DbHelper.GetStringOrNull(r, "payment_gateway"),
                },
                new Dictionary<string, object?> { ["id"] = req.TransactionId });

            if (tx == null) return (false, "Transaction nahi mila");
            if (tx.ReceiverId != userId) return (false, "Unauthorized");
            // BUG#2 FIX: idempotent — already completed → return success (not error)
            if (tx.Status == "completed") return (true, "Coins pehle se credited hain");
            if (tx.Status != "pending") return (false, "Transaction process nahi ho sakta");

            // BUG#1 FIX: Verify HMAC signature for Razorpay
            if (string.Equals(tx.Gateway, "razorpay", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(req.GatewaySignature))
                    return (false, "Payment signature missing hai");
                if (string.IsNullOrEmpty(tx.OrderId))
                    return (false, "Order ID nahi mila. Support se contact karein.");
                if (!VerifyRazorpaySignature(tx.OrderId, req.GatewayTransactionId, req.GatewaySignature))
                    return (false, "Payment signature invalid hai. Fraudulent request detected.");
            }

            // Mark transaction as completed
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"UPDATE coin_transactions
                  SET status = 'completed', gateway_transaction_id = @gtid,
                      completed_at = NOW()
                  WHERE id = @id",
                new Dictionary<string, object?>
                {
                    ["id"]   = req.TransactionId,
                    ["gtid"] = req.GatewayTransactionId
                }, transaction);

            // Credit coins to wallet (with balance_before tracking)
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO wallets (user_id, coin_balance, total_earned, last_transaction_at)
                  VALUES (@uid, @amount, @amount, NOW())
                  ON CONFLICT (user_id) DO UPDATE
                  SET coin_balance          = wallets.coin_balance + @amount,
                      total_earned          = wallets.total_earned + @amount,
                      last_transaction_at   = NOW(),
                      updated_at            = NOW()",
                new Dictionary<string, object?>
                {
                    ["uid"]    = userId,
                    ["amount"] = tx.Amount
                }, transaction);

            await transaction.CommitAsync();
            return (true, $"{tx.Amount} coins wallet mein add ho gaye!");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return (false, $"Recharge verify error: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message, AppreciationResponse? Data)> AppreciateAsync(Guid senderId, AppreciateRequest req)
    {
        if (req.ReceiverId == senderId) return (false, "Apne aap ko appreciate nahi kar sakte", null);

        using var conn = await _db.CreateConnectionAsync();
        using var transaction = await conn.BeginTransactionAsync();

        try
        {
            // Check sender wallet
            var senderWallet = await DbHelper.ExecuteReaderFirstAsync(conn,
                "SELECT coin_balance, is_frozen FROM wallets WHERE user_id = @uid FOR UPDATE",
                r => new { Balance = DbHelper.GetLong(r, "coin_balance"), IsFrozen = DbHelper.GetBool(r, "is_frozen") },
                new Dictionary<string, object?> { ["uid"] = senderId });

            if (senderWallet == null) return (false, "Wallet nahi mila", null);
            if (senderWallet.IsFrozen) return (false, "Aapka wallet freeze hai", null);
            if (senderWallet.Balance < req.CoinAmount) return (false, "Insufficient coins", null);

            // BUG#7 FIX: Check receiver exists AND is active (not banned/suspended)
            var receiverStatus = await DbHelper.ExecuteReaderFirstAsync(conn,
                "SELECT status FROM users WHERE id = @uid AND deleted_at IS NULL",
                r => DbHelper.GetString(r, "status"),
                new Dictionary<string, object?> { ["uid"] = req.ReceiverId });
            if (receiverStatus == null) return (false, "Receiver nahi mila", null);
            if (receiverStatus != "active")
                return (false, "Is creator ko abhi appreciation nahi bhej sakte", null);

            // BUG#8 FIX: Rate limit — max 20 appreciations per sender per hour
            var recentCount = await DbHelper.ExecuteScalarAsync<long>(conn,
                @"SELECT COUNT(*) FROM coin_transactions
                  WHERE sender_id = @sid AND transaction_type = 'appreciation'
                    AND created_at >= NOW() - INTERVAL '1 hour'",
                new Dictionary<string, object?> { ["sid"] = senderId });
            if (recentCount >= 20)
                return (false, "Aap ek ghante mein sirf 20 appreciations bhej sakte hain", null);

            // BUG#8 FIX: Circular transfer detection — A→B check if B→A happened recently
            var circularExists = await DbHelper.ExecuteScalarAsync<bool>(conn,
                @"SELECT EXISTS(
                    SELECT 1 FROM coin_transactions
                    WHERE sender_id = @rid AND receiver_id = @sid
                      AND transaction_type = 'appreciation'
                      AND created_at >= NOW() - INTERVAL '24 hours'
                  )",
                new Dictionary<string, object?> { ["rid"] = req.ReceiverId, ["sid"] = senderId });
            if (circularExists)
            {
                // Flag as suspicious but don't block — insert fraud alert
                await DbHelper.ExecuteNonQueryAsync(conn,
                    @"INSERT INTO fraud_alerts (user_id, alert_type, severity)
                      VALUES (@uid, 'circular_appreciation', 'high')
                      ON CONFLICT DO NOTHING",
                    new Dictionary<string, object?> { ["uid"] = senderId });
            }

            // Revenue split: 40% creator, 40% platform, 20% reward fund
            long creatorShare = (long)(req.CoinAmount * 0.40m);
            long platformShare = (long)(req.CoinAmount * 0.40m);
            long rewardFundShare = req.CoinAmount - creatorShare - platformShare;

            // Create transaction record
            var txId = Guid.NewGuid();
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO coin_transactions
                    (id, sender_id, receiver_id, transaction_type, status, amount,
                     creator_share, platform_share, reward_fund_share,
                     reference_type, reference_id, description, completed_at)
                  VALUES
                    (@id, @sid, @rid, 'appreciation', 'completed', @amount,
                     @cs, @ps, @rfs,
                     @refType, @refId, @desc, NOW())",
                new Dictionary<string, object?>
                {
                    ["id"] = txId,
                    ["sid"] = senderId,
                    ["rid"] = req.ReceiverId,
                    ["amount"] = req.CoinAmount,
                    ["cs"] = creatorShare,
                    ["ps"] = platformShare,
                    ["rfs"] = rewardFundShare,
                    ["refType"] = req.StoryId.HasValue ? "story" : (object?)DBNull.Value,
                    ["refId"] = (object?)req.StoryId ?? (object?)req.EpisodeId ?? DBNull.Value,
                    ["desc"] = $"Appreciation: {req.CoinAmount} coins"
                }, transaction);

            // Deduct from sender
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE wallets SET coin_balance = coin_balance - @amount, total_spent = total_spent + @amount, last_transaction_at = NOW(), updated_at = NOW() WHERE user_id = @uid",
                new Dictionary<string, object?> { ["uid"] = senderId, ["amount"] = req.CoinAmount }, transaction);

            // Credit to receiver (only creator share)
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO wallets (user_id, coin_balance, total_earned, last_transaction_at)
                  VALUES (@uid, @amount, @amount, NOW())
                  ON CONFLICT (user_id) DO UPDATE
                  SET coin_balance = wallets.coin_balance + @amount,
                      total_earned = wallets.total_earned + @amount,
                      last_transaction_at = NOW(),
                      updated_at = NOW()",
                new Dictionary<string, object?> { ["uid"] = req.ReceiverId, ["amount"] = creatorShare }, transaction);

            // Update reward fund
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE reward_fund_pool SET balance = balance + @amount, total_deposited = total_deposited + @amount, last_updated = NOW()",
                new Dictionary<string, object?> { ["amount"] = rewardFundShare }, transaction);

            // Insert appreciation record
            var appId = Guid.NewGuid();
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO appreciations (id, sender_id, receiver_id, story_id, episode_id, coin_amount, message, transaction_id)
                  VALUES (@id, @sid, @rid, @storyId, @episodeId, @amount, @msg, @txId)",
                new Dictionary<string, object?>
                {
                    ["id"] = appId,
                    ["sid"] = senderId,
                    ["rid"] = req.ReceiverId,
                    ["storyId"] = (object?)req.StoryId ?? DBNull.Value,
                    ["episodeId"] = (object?)req.EpisodeId ?? DBNull.Value,
                    ["amount"] = req.CoinAmount,
                    ["msg"] = (object?)req.Message ?? DBNull.Value,
                    ["txId"] = txId
                }, transaction);

            // Update story coins earned if applicable
            if (req.StoryId.HasValue)
            {
                await DbHelper.ExecuteNonQueryAsync(conn,
                    "UPDATE stories SET total_coins_earned = total_coins_earned + @amount WHERE id = @id",
                    new Dictionary<string, object?> { ["id"] = req.StoryId.Value, ["amount"] = req.CoinAmount }, transaction);
            }

            await transaction.CommitAsync();

            // Fetch receiver username
            var receiverUsername = await DbHelper.ExecuteScalarAsync<string>(conn,
                "SELECT username FROM users WHERE id = @uid",
                new Dictionary<string, object?> { ["uid"] = req.ReceiverId });

            return (true, $"{creatorShare} coins {receiverUsername} ko mile!", new AppreciationResponse
            {
                Id = appId,
                ReceiverId = req.ReceiverId,
                ReceiverUsername = receiverUsername ?? "",
                CoinAmount = req.CoinAmount,
                Message = req.Message,
                CreatedAt = DateTime.UtcNow
            });
        }
        catch
        {
            await transaction.RollbackAsync();
            return (false, "Appreciation mein error aaya", null);
        }
    }

    public async Task<(bool Success, string Message, WithdrawalResponse? Data)> RequestWithdrawalAsync(Guid userId, WithdrawalRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();

        // Validate payment method
        if (req.PaymentMethod == "upi" && string.IsNullOrEmpty(req.UpiId))
            return (false, "UPI ID required hai", null);
        if (req.PaymentMethod == "bank_transfer" &&
            (string.IsNullOrEmpty(req.BankAccountNumber) || string.IsNullOrEmpty(req.BankIfsc) || string.IsNullOrEmpty(req.AccountHolderName)))
            return (false, "Bank details incomplete hain", null);

        // BUG#9 FIX: Check user is not banned/suspended before allowing withdrawal
        var userStatus = await DbHelper.ExecuteScalarAsync<string>(conn,
            "SELECT status FROM users WHERE id = @uid",
            new Dictionary<string, object?> { ["uid"] = userId });
        if (userStatus == null) return (false, "User nahi mila", null);
        if (userStatus != "active")
            return (false, "Aapka account suspend hai. Withdrawal allowed nahi hai.", null);

        // Check wallet
        var wallet = await DbHelper.ExecuteReaderFirstAsync(conn,
            "SELECT coin_balance, is_frozen, pending_withdrawal FROM wallets WHERE user_id = @uid FOR UPDATE",
            r => new
            {
                Balance  = DbHelper.GetLong(r, "coin_balance"),
                IsFrozen = DbHelper.GetBool(r, "is_frozen"),
                Pending  = DbHelper.GetLong(r, "pending_withdrawal")
            },
            new Dictionary<string, object?> { ["uid"] = userId });

        if (wallet == null) return (false, "Wallet nahi mila", null);
        if (wallet.IsFrozen) return (false, "Wallet freeze hai. Support se contact karein.", null);
        if (wallet.Balance < req.CoinAmount) return (false, "Insufficient coins", null);
        if (wallet.Pending > 0) return (false, "Ek pending withdrawal already hai. Pehle complete hone do.", null);

        decimal inrAmount = req.CoinAmount * CoinToInrRate;
        decimal tdsAmount = inrAmount * TdsRate;
        decimal netAmount = inrAmount - tdsAmount;

        using var transaction = await conn.BeginTransactionAsync();
        try
        {
            var wdId = Guid.NewGuid();
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO withdrawals
                    (id, user_id, coin_amount, inr_amount, status, payment_method,
                     upi_id, bank_name, bank_account_number, bank_ifsc, account_holder_name,
                     tds_amount, net_amount)
                  VALUES
                    (@id, @uid, @coins, @inr, 'pending', @method,
                     @upi, @bank, @accNum, @ifsc, @holder,
                     @tds, @net)",
                new Dictionary<string, object?>
                {
                    ["id"] = wdId,
                    ["uid"] = userId,
                    ["coins"] = req.CoinAmount,
                    ["inr"] = inrAmount,
                    ["method"] = req.PaymentMethod,
                    ["upi"] = (object?)req.UpiId ?? DBNull.Value,
                    ["bank"] = (object?)req.BankName ?? DBNull.Value,
                    ["accNum"] = (object?)req.BankAccountNumber ?? DBNull.Value,
                    ["ifsc"] = (object?)req.BankIfsc ?? DBNull.Value,
                    ["holder"] = (object?)req.AccountHolderName ?? DBNull.Value,
                    ["tds"] = tdsAmount,
                    ["net"] = netAmount
                }, transaction);

            // Hold coins as pending
            await DbHelper.ExecuteNonQueryAsync(conn,
                "UPDATE wallets SET coin_balance = coin_balance - @amount, pending_withdrawal = pending_withdrawal + @amount, updated_at = NOW() WHERE user_id = @uid",
                new Dictionary<string, object?> { ["uid"] = userId, ["amount"] = req.CoinAmount }, transaction);

            await transaction.CommitAsync();

            return (true, "Withdrawal request submit ho gayi! 3-5 business days mein process hogi.", new WithdrawalResponse
            {
                Id = wdId,
                CoinAmount = req.CoinAmount,
                InrAmount = inrAmount,
                TdsAmount = tdsAmount,
                NetAmount = netAmount,
                Status = "pending",
                PaymentMethod = req.PaymentMethod,
                UpiId = req.UpiId,
                BankName = req.BankName,
                AccountHolderName = req.AccountHolderName,
                CreatedAt = DateTime.UtcNow
            });
        }
        catch
        {
            await transaction.RollbackAsync();
            return (false, "Withdrawal request mein error aaya", null);
        }
    }

    public async Task<PagedResult<WithdrawalResponse>> GetWithdrawalHistoryAsync(Guid userId, int page, int pageSize)
    {
        using var conn = await _db.CreateConnectionAsync();
        pageSize = Math.Min(pageSize, 50);
        int offset = (page - 1) * pageSize;

        var total = await DbHelper.ExecuteScalarAsync<long>(conn,
            "SELECT COUNT(*) FROM withdrawals WHERE user_id = @uid",
            new Dictionary<string, object?> { ["uid"] = userId });

        var items = await DbHelper.ExecuteReaderAsync(conn,
            @"SELECT * FROM withdrawals WHERE user_id = @uid
              ORDER BY created_at DESC LIMIT @limit OFFSET @offset",
            r => new WithdrawalResponse
            {
                Id = DbHelper.GetGuid(r, "id"),
                CoinAmount = DbHelper.GetLong(r, "coin_amount"),
                InrAmount = DbHelper.GetDecimal(r, "inr_amount"),
                GstAmount = DbHelper.GetDecimal(r, "gst_amount"),
                TdsAmount = DbHelper.GetDecimal(r, "tds_amount"),
                NetAmount = DbHelper.GetDecimal(r, "net_amount"),
                Status = DbHelper.GetString(r, "status"),
                PaymentMethod = DbHelper.GetString(r, "payment_method"),
                UpiId = DbHelper.GetStringOrNull(r, "upi_id"),
                BankName = DbHelper.GetStringOrNull(r, "bank_name"),
                AccountHolderName = DbHelper.GetStringOrNull(r, "account_holder_name"),
                RejectionReason = DbHelper.GetStringOrNull(r, "rejection_reason"),
                TransactionReference = DbHelper.GetStringOrNull(r, "transaction_reference"),
                CreatedAt = DbHelper.GetDateTime(r, "created_at"),
                ProcessedAt = DbHelper.GetDateTimeOrNull(r, "processed_at")
            },
            new Dictionary<string, object?> { ["uid"] = userId, ["limit"] = pageSize, ["offset"] = offset });

        return PagedResult<WithdrawalResponse>.Create(items, (int)total, page, pageSize);
    }
}
