using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Modules.Wallet.Models;
using HauntedVoiceUniverse.Modules.Wallet.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HauntedVoiceUniverse.Modules.Wallet.Controllers;

[ApiController]
[Route("api/wallet")]
[Authorize]
[Produces("application/json")]
public class WalletController : ControllerBase
{
    private readonly IWalletService _walletService;

    public WalletController(IWalletService walletService)
    {
        _walletService = walletService;
    }

    private Guid UserId => Guid.Parse(User.FindFirstValue("uid")!);

    // GET /api/wallet
    /// <summary>Apna wallet dekho</summary>
    [HttpGet]
    public async Task<IActionResult> GetWallet()
    {
        var wallet = await _walletService.GetWalletAsync(UserId);
        return Ok(ApiResponse<WalletResponse>.Ok(wallet!));
    }

    // GET /api/wallet/transactions
    /// <summary>Transaction history dekho</summary>
    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactions(
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20,
        [FromQuery] string? type = null)
    {
        var result = await _walletService.GetTransactionsAsync(UserId, page, page_size, type);
        return Ok(ApiResponse<PagedResult<TransactionResponse>>.Ok(result));
    }

    // GET /api/wallet/recharge-packs
    /// <summary>Available recharge packs</summary>
    [HttpGet("recharge-packs")]
    [AllowAnonymous]
    public async Task<IActionResult> GetRechargePacks()
    {
        var packs = await _walletService.GetRechargePacksAsync();
        return Ok(ApiResponse<List<RechargePackResponse>>.Ok(packs));
    }

    // GET /api/wallet/appreciation-stickers
    /// <summary>Appreciation sticker catalog</summary>
    [HttpGet("appreciation-stickers")]
    [AllowAnonymous]
    public async Task<IActionResult> GetAppreciationStickers()
    {
        var stickers = await _walletService.GetAppreciationStickersAsync();
        return Ok(ApiResponse<List<AppreciationStickerResponse>>.Ok(stickers));
    }

    // GET /api/wallet/story/{storyId}/appreciations
    /// <summary>Story ki total appreciation stats</summary>
    [HttpGet("story/{storyId:guid}/appreciations")]
    [AllowAnonymous]
    public async Task<IActionResult> GetStoryAppreciations(Guid storyId)
    {
        var result = await _walletService.GetStoryAppreciationsAsync(storyId);
        return Ok(ApiResponse<StoryAppreciationsResponse>.Ok(result));
    }

    // POST /api/wallet/recharge/initiate
    /// <summary>Recharge initiate karo (order create)</summary>
    [HttpPost("recharge/initiate")]
    public async Task<IActionResult> InitiateRecharge([FromBody] InitiateRechargeRequest req)
    {
        var (success, message, data) = await _walletService.InitiateRechargeAsync(UserId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<InitiateRechargeResponse>.Ok(data!, message));
    }

    // POST /api/wallet/recharge/verify
    /// <summary>Payment verify karo aur coins credit karo</summary>
    [HttpPost("recharge/verify")]
    public async Task<IActionResult> VerifyRecharge([FromBody] VerifyRechargeRequest req)
    {
        var (success, message) = await _walletService.VerifyRechargeAsync(UserId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // POST /api/wallet/appreciate
    /// <summary>Creator ko appreciation coins bhejo</summary>
    [HttpPost("appreciate")]
    public async Task<IActionResult> Appreciate([FromBody] AppreciateRequest req)
    {
        var (success, message, data) = await _walletService.AppreciateAsync(UserId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<AppreciationResponse>.Ok(data!, message));
    }

    // POST /api/wallet/withdrawal/request
    /// <summary>Withdrawal request karo</summary>
    [HttpPost("withdrawal/request")]
    public async Task<IActionResult> RequestWithdrawal([FromBody] WithdrawalRequest req)
    {
        var (success, message, data) = await _walletService.RequestWithdrawalAsync(UserId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<WithdrawalResponse>.Ok(data!, message));
    }

    // GET /api/wallet/withdrawal/history
    /// <summary>Withdrawal history dekho</summary>
    [HttpGet("withdrawal/history")]
    public async Task<IActionResult> GetWithdrawalHistory(
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
    {
        var result = await _walletService.GetWithdrawalHistoryAsync(UserId, page, page_size);
        return Ok(ApiResponse<PagedResult<WithdrawalResponse>>.Ok(result));
    }
}
