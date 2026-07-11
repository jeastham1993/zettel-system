using Microsoft.AspNetCore.Mvc;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Controllers;

/// <summary>Chat sessions and conversations with the knowledge base.</summary>
[ApiController]
[Route("api/chat")]
[Produces("application/json")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;

    public ChatController(IChatService chatService)
    {
        _chatService = chatService;
    }

    /// <summary>Create a new chat session.</summary>
    /// <param name="request">Creation request with optional title and context notes.</param>
    /// <response code="201">Returns the created chat session.</response>
    [HttpPost("sessions")]
    [ProducesResponseType<ChatSession>(201)]
    public async Task<IActionResult> CreateSession([FromBody] CreateChatSessionRequest request)
    {
        var session = await _chatService.CreateSessionAsync(request);
        return CreatedAtAction(nameof(GetSession), new { sessionId = session.Id }, session);
    }

    /// <summary>Get a chat session by ID.</summary>
    /// <param name="sessionId">The session ID.</param>
    /// <response code="200">Returns the chat session.</response>
    /// <response code="404">Session not found.</response>
    [HttpGet("sessions/{sessionId}")]
    [ProducesResponseType<ChatSession>(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetSession(string sessionId)
    {
        var session = await _chatService.GetSessionAsync(sessionId);
        if (session == null)
        {
            return NotFound();
        }
        return Ok(session);
    }

    /// <summary>List all chat sessions.</summary>
    /// <param name="status">Optional filter by status.</param>
    /// <param name="skip">Number of sessions to skip.</param>
    /// <param name="take">Number of sessions to return.</param>
    /// <response code="200">Returns list of chat sessions.</response>
    [HttpGet("sessions")]
    [ProducesResponseType<IReadOnlyList<ChatSession>>(200)]
    public async Task<IActionResult> ListSessions(
        [FromQuery] ChatSessionStatus? status = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50)
    {
        var sessions = await _chatService.ListSessionsAsync(status, skip, take);
        return Ok(sessions);
    }

    /// <summary>Update a chat session.</summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="request">Update request with optional title and status.</param>
    /// <response code="200">Returns the updated session.</response>
    /// <response code="404">Session not found.</response>
    [HttpPut("sessions/{sessionId}")]
    [ProducesResponseType<ChatSession>(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> UpdateSession(
        string sessionId,
        [FromBody] UpdateChatSessionRequest request)
    {
        var session = await _chatService.UpdateSessionAsync(sessionId, request.Title, request.Status);
        if (session == null)
        {
            return NotFound();
        }
        return Ok(session);
    }

    /// <summary>Delete a chat session.</summary>
    /// <param name="sessionId">The session ID.</param>
    /// <response code="204">Session deleted.</response>
    /// <response code="404">Session not found.</response>
    [HttpDelete("sessions/{sessionId}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteSession(string sessionId)
    {
        var deleted = await _chatService.DeleteSessionAsync(sessionId);
        if (!deleted)
        {
            return NotFound();
        }
        return NoContent();
    }

    /// <summary>Send a message to a chat session.</summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="request">The message request.</param>
    /// <response code="200">Returns the assistant's response.</response>
    /// <response code="404">Session not found.</response>
    [HttpPost("sessions/{sessionId}/messages")]
    [ProducesResponseType<ChatMessageResponse>(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> SendMessage(
        string sessionId,
        [FromBody] SendChatMessageRequest request)
    {
        try
        {
            var response = await _chatService.SendMessageAsync(sessionId, request);
            return Ok(response);
        }
        catch (ArgumentException ex) when (ex.ParamName == "sessionId")
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>Get messages for a chat session.</summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="skip">Number of messages to skip.</param>
    /// <param name="take">Number of messages to return.</param>
    /// <response code="200">Returns list of messages.</response>
    [HttpGet("sessions/{sessionId}/messages")]
    [ProducesResponseType<IReadOnlyList<ChatMessageResponse>>(200)]
    public async Task<IActionResult> GetMessages(
        string sessionId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50)
    {
        var messages = await _chatService.GetMessagesAsync(sessionId, skip, take);
        return Ok(messages);
    }

    /// <summary>Regenerate the last assistant response.</summary>
    /// <param name="sessionId">The session ID.</param>
    /// <response code="200">Returns the new assistant response.</response>
    /// <response code="404">Session not found or no user message to regenerate for.</response>
    [HttpPost("sessions/{sessionId}/regenerate")]
    [ProducesResponseType<ChatMessageResponse>(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> RegenerateResponse(string sessionId)
    {
        try
        {
            var response = await _chatService.RegenerateResponseAsync(sessionId);
            return Ok(response);
        }
        catch (ArgumentException ex) when (ex.ParamName == "sessionId")
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }
}

/// <summary>Request to update a chat session.</summary>
public class UpdateChatSessionRequest
{
    /// <summary>Optional new title.</summary>
    public string? Title { get; set; }
    /// <summary>Optional new status.</summary>
    public ChatSessionStatus? Status { get; set; }
}