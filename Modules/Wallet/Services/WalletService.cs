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

        // Create pending transaction
        var txId = Guid.NewGuid();
        var orderId = $"HVU-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}-{txId.ToString()[..8].ToUpper()}";

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

    public async Task<(bool Success, string Message)> VerifyRechargeAsync(Guid userId, VerifyRechargeRequest req)
    {
        using var conn = await _db.CreateConnectionAsync();
        using var transaction = await conn.BeginTransactionAsync();

        try
        {
            // Lock the transaction row
            var tx = await DbHelper.ExecuteReaderFirstAsync(conn,
                "SELECT id, receiver_id, amount, status FROM coin_transactions WHERE id = @id FOR UPDATE",
                r => new
                {
                    Id = DbHelper.GetGuid(r, "id"),
                    ReceiverId = DbHelper.GetGuid(r, "receiver_id"),
                    Amount = DbHelper.GetLong(r, "amount"),
                    Status = DbHelper.GetString(r, "status")
                },
                new Dictionary<string, object?> { ["id"] = req.TransactionId });

            if (tx == null) return (false, "Transaction nahi mila");
            if (tx.ReceiverId != userId) return (false, "Unauthorized");
            if (tx.Status != "pending") return (false, "Transaction already processed hai");

            // Mark transaction as completed
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"UPDATE coin_transactions
                  SET status = 'completed', gateway_transaction_id = @gtid,
                      completed_at = NOW(), updated_at = NOW()
                  WHERE id = @id",
                new Dictionary<string, object?>
                {
                    ["id"] = req.TransactionId,
                    ["gtid"] = req.GatewayTransactionId
                }, transaction);

            // Credit coins to wallet
            await DbHelper.ExecuteNonQueryAsync(conn,
                @"INSERT INTO wallets (user_id, coin_balance, total_earned, last_transaction_at)
                  VALUES (@uid, @amount, @amount, NOW())
                  ON CONFLICT (user_id) DO UPDATE
                  SET coin_balance = wallets.coin_balance + @amount,
                      total_earned = wallets.total_earned + @amount,
                      last_transaction_at = NOW(),
                      updated_at = NOW()",
                new Dictionary<string, object?>
                {
                    ["uid"] = userId,
                    ["amount"] = tx.Amount
                }, transaction);

            await transaction.CommitAsync();
            return (true, $"{tx.Amount} coins wallet mein add ho gaye!");
        }
        catch
        {
            await transaction.RollbackAsync();
            return (false, "Recharge verify karne mein error aaya. Support se contact karein.");
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

            // Check receiver exists
            var receiverExists = await DbHelper.ExecuteScalarAsync<bool>(conn,
                "SELECT EXISTS(SELECT 1 FROM users WHERE id = @uid AND deleted_at IS NULL)",
                new Dictionary<string, object?> { ["uid"] = req.ReceiverId });
            if (!receiverExists) return (false, "Receiver nahi mila", null);

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

        // Check wallet
        var wallet = await DbHelper.ExecuteReaderFirstAsync(conn,
            "SELECT coin_balance, is_frozen, pending_withdrawal FROM wallets WHERE user_id = @uid FOR UPDATE",
            r => new
            {
                Balance = DbHelper.GetLong(r, "coin_balance"),
                IsFrozen = DbHelper.GetBool(r, "is_frozen"),
                Pending = DbHelper.GetLong(r, "pending_withdrawal")
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
