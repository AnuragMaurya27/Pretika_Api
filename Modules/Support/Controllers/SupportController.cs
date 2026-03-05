using HauntedVoiceUniverse.Common;
using HauntedVoiceUniverse.Modules.Support.Models;
using HauntedVoiceUniverse.Modules.Support.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HauntedVoiceUniverse.Modules.Support.Controllers;

[ApiController]
[Route("api/support")]
[Produces("application/json")]
public class SupportController : ControllerBase
{
    private readonly ISupportService _supportService;

    public SupportController(ISupportService supportService)
    {
        _supportService = supportService;
    }

    private Guid UserId => Guid.Parse(User.FindFirstValue("uid")!);

    // GET /api/support/categories
    /// <summary>Support categories</summary>
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories()
    {
        var cats = await _supportService.GetCategoriesAsync();
        return Ok(ApiResponse<List<SupportCategoryResponse>>.Ok(cats));
    }

    // POST /api/support/tickets
    /// <summary>Naya support ticket banao</summary>
    [HttpPost("tickets")]
    [Authorize]
    public async Task<IActionResult> CreateTicket([FromBody] CreateTicketRequest req)
    {
        var (success, message, data) = await _supportService.CreateTicketAsync(UserId, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Created("", ApiResponse<TicketResponse>.Created(data!, message));
    }

    // GET /api/support/tickets
    /// <summary>Apne tickets dekho</summary>
    [HttpGet("tickets")]
    [Authorize]
    public async Task<IActionResult> GetMyTickets(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 20)
    {
        var result = await _supportService.GetMyTicketsAsync(UserId, status, page, page_size);
        return Ok(ApiResponse<PagedResult<TicketResponse>>.Ok(result));
    }

    // GET /api/support/tickets/{id}
    /// <summary>Ticket detail dekho</summary>
    [HttpGet("tickets/{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetTicket(Guid id)
    {
        var ticket = await _supportService.GetTicketAsync(UserId, id);
        if (ticket == null) return NotFound(ApiResponse<object>.NotFound("Ticket nahi mila"));
        return Ok(ApiResponse<TicketResponse>.Ok(ticket));
    }

    // GET /api/support/tickets/{id}/messages
    /// <summary>Ticket messages dekho</summary>
    [HttpGet("tickets/{id:guid}/messages")]
    [Authorize]
    public async Task<IActionResult> GetMessages(Guid id)
    {
        var messages = await _supportService.GetTicketMessagesAsync(UserId, id);
        return Ok(ApiResponse<List<TicketMessageResponse>>.Ok(messages));
    }

    // POST /api/support/tickets/{id}/messages
    /// <summary>Ticket mein message bhejo</summary>
    [HttpPost("tickets/{id:guid}/messages")]
    [Authorize]
    public async Task<IActionResult> AddMessage(Guid id, [FromBody] AddTicketMessageRequest req)
    {
        var (success, message, data) = await _supportService.AddMessageAsync(UserId, id, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<TicketMessageResponse>.Ok(data!, message));
    }

    // POST /api/support/tickets/{id}/close
    /// <summary>Ticket close karo</summary>
    [HttpPost("tickets/{id:guid}/close")]
    [Authorize]
    public async Task<IActionResult> CloseTicket(Guid id)
    {
        var (success, message) = await _supportService.CloseTicketAsync(UserId, id);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }

    // POST /api/support/tickets/{id}/rate
    /// <summary>Support experience rate karo</summary>
    [HttpPost("tickets/{id:guid}/rate")]
    [Authorize]
    public async Task<IActionResult> RateTicket(Guid id, [FromBody] RateTicketRequest req)
    {
        var (success, message) = await _supportService.RateTicketAsync(UserId, id, req);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message));
        return Ok(ApiResponse<object>.Ok(null, message));
    }
}
