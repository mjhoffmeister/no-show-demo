using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using NoShowPredictor.Agent.Services;
using System.Collections.Concurrent;

namespace NoShowPredictor.Agent.Controllers;

/// <summary>
/// Controller for chat interactions with the NoShow Predictor agent.
/// FR-015: No persistent storage of patient identifiers - session-only state.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IAgentChatService _agentService;
    private readonly ILogger<ChatController> _logger;

    // In-memory conversation storage (session-only per FR-015)
    // In production, would use distributed cache with short TTL
    private static readonly ConcurrentDictionary<string, List<ConversationMessage>> _conversations = new();

    public ChatController(IAgentChatService agentService, ILogger<ChatController> logger)
    {
        _agentService = agentService ?? throw new ArgumentNullException(nameof(agentService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Send a chat message to the agent and receive a response.
    /// </summary>
    /// <param name="request">The chat request containing the user message</param>
    /// <returns>The agent's response</returns>
    [HttpPost]
    [ProducesResponseType(typeof(ChatResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SendMessage([FromBody] ChatRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "Message cannot be empty" });
        }

        // Generate or use existing conversation ID
        var conversationId = request.ConversationId ?? Guid.NewGuid().ToString();
        
        // Get or create conversation history
        var history = _conversations.GetOrAdd(conversationId, _ => new List<ConversationMessage>());

        // Add user message to history
        var userMessage = new ConversationMessage
        {
            Role = "user",
            Content = request.Message,
            Timestamp = DateTime.UtcNow
        };
        history.Add(userMessage);

        try
        {
            _logger.LogInformation("Processing chat message for conversation {ConversationId}", conversationId);

            // Build chat history for the agent
            var chatMessages = history.Select(m => new ChatMessage(
                m.Role == "user" ? ChatRole.User : ChatRole.Assistant,
                m.Content
            )).ToList();

            // Get response from agent service
            var response = await _agentService.ChatAsync(chatMessages);
            var responseText = response.Text ?? "I apologize, but I couldn't generate a response. Please try again.";

            // Add assistant response to history
            var assistantMessage = new ConversationMessage
            {
                Role = "assistant",
                Content = responseText,
                Timestamp = DateTime.UtcNow
            };
            history.Add(assistantMessage);

            // Clean up old conversations (simple TTL-like behavior)
            CleanupOldConversations();

            return Ok(new ChatResponseDto
            {
                ConversationId = conversationId,
                Response = responseText,
                ReferencedAppointments = null,
                SuggestedActions = null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chat message for conversation {ConversationId}", conversationId);
            return StatusCode(500, new { error = "An error occurred processing your request" });
        }
    }

    /// <summary>
    /// Get conversation history for a given conversation ID.
    /// </summary>
    [HttpGet("{conversationId}/history")]
    [ProducesResponseType(typeof(List<ConversationMessage>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetHistory(string conversationId)
    {
        if (_conversations.TryGetValue(conversationId, out var history))
        {
            return Ok(history.ToList());
        }

        return NotFound(new { error = "Conversation not found" });
    }

    /// <summary>
    /// Delete a conversation (clear history).
    /// </summary>
    [HttpDelete("{conversationId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult DeleteConversation(string conversationId)
    {
        _conversations.TryRemove(conversationId, out _);
        return NoContent();
    }

    /// <summary>
    /// Clean up conversations older than 1 hour (FR-015 compliance).
    /// </summary>
    private static void CleanupOldConversations()
    {
        var cutoff = DateTime.UtcNow.AddHours(-1);
        
        foreach (var kvp in _conversations)
        {
            if (kvp.Value.Count > 0 && kvp.Value[^1].Timestamp < cutoff)
            {
                _conversations.TryRemove(kvp.Key, out _);
            }
        }
    }
}

#region Request/Response Models

public sealed record ChatRequestDto
{
    public required string Message { get; init; }
    public string? ConversationId { get; init; }
    public int? AppointmentId { get; init; }
}

public sealed record ChatResponseDto
{
    public required string ConversationId { get; init; }
    public required string Response { get; init; }
    public IReadOnlyList<AppointmentDto>? ReferencedAppointments { get; init; }
    public IReadOnlyList<RecommendationDto>? SuggestedActions { get; init; }
}

public sealed record ConversationMessage
{
    public required string Role { get; init; }
    public required string Content { get; init; }
    public required DateTime Timestamp { get; init; }
}

public record AppointmentDto
{
    public required int AppointmentId { get; init; }
    public required DateOnly AppointmentDate { get; init; }
    public required string AppointmentStartTime { get; init; }
    public required string AppointmentTypeName { get; init; }
    public required string AppointmentStatus { get; init; }
    public required string PatientAgeBucket { get; init; }
    public required string PatientGender { get; init; }
    public required string ProviderName { get; init; }
    public required string DepartmentName { get; init; }
    public double? NoShowProbability { get; init; }
    public string? RiskLevel { get; init; }
}

public sealed record RecommendationDto
{
    public required int RecommendationId { get; init; }
    public required string ActionType { get; init; }
    public required int Priority { get; init; }
    public required string Description { get; init; }
    public required string Rationale { get; init; }
    public bool? ActionTaken { get; init; }
}

#endregion
