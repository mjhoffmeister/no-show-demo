using Microsoft.Extensions.AI;
using NoShowPredictor.Agent.Data;
using NoShowPredictor.Agent.Tools;

namespace NoShowPredictor.Agent.Services;

/// <summary>
/// Service that wraps the chat client with agent tools and system prompt.
/// Handles the coordination between chat requests and tool invocations.
/// </summary>
public interface IAgentChatService
{
    /// <summary>
    /// Sends a message to the agent and gets a response.
    /// </summary>
    /// <param name="messages">The conversation history</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The assistant's response</returns>
    Task<ChatResponse> ChatAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of the agent chat service using Microsoft.Extensions.AI.
/// </summary>
public sealed class AgentChatService : IAgentChatService
{
    private readonly IChatClient _chatClient;
    private readonly ChatOptions _chatOptions;
    private readonly ILogger<AgentChatService> _logger;

    private const string SystemPrompt = """
        You are a scheduling coordinator assistant for a healthcare clinic. Your role is to help staff identify patients at risk of missing their appointments and recommend actions to reduce no-shows.

        ## Capabilities
        - Query appointment schedules for specific dates or date ranges
        - Retrieve no-show probability predictions for upcoming appointments
        - Explain risk factors contributing to high no-show predictions
        - Recommend actions like confirmation calls, reminders, or overbooking strategies
        - Look up individual patient appointment history and risk profiles

        ## Guidelines
        1. Always include the no-show probability percentage when discussing appointment risk
        2. Prioritize recommendations by impact (high-risk patients first)
        3. When listing appointments, include: patient name, time, provider, and risk level
        4. Explain risk factors in plain language (e.g., "Patient has missed 3 of last 5 appointments")
        5. For date queries, confirm the interpreted date in your response
        6. If predictions are unavailable, indicate this and provide historical data instead

        ## Data Access
        You have access to tools to query:
        - Appointment schedules (past and future)
        - ML model predictions for no-show probability

        ## Constraints
        - Never reveal raw patient IDs; use patient names or "Patient #X" format only
        - All data is synthetic for demonstration purposes
        - Predictions are estimates; recommend verification for critical decisions
        
        ## Response Format
        When listing high-risk appointments, use a clear format like:
        
        **High-Risk Appointments for [Date]:**
        1. **[Time]** - Patient #X with Dr. Y
           - Risk: XX% (High/Medium)
           - Factors: [brief explanation]
        
        Always be helpful, concise, and actionable.
        """;

    public AgentChatService(
        IChatClient chatClient,
        IAppointmentRepository appointmentRepository,
        IMLEndpointClient mlEndpointClient,
        ILogger<AgentChatService> logger,
        ILoggerFactory loggerFactory)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create tool instances
        var appointmentTool = new AppointmentTool(
            appointmentRepository,
            loggerFactory.CreateLogger<AppointmentTool>());

        var predictionTool = new PredictionTool(
            appointmentRepository,
            mlEndpointClient,
            loggerFactory.CreateLogger<PredictionTool>());

        // Configure chat options with tools
        _chatOptions = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(
                    appointmentTool.GetAppointmentsByDateRange,
                    new AIFunctionFactoryOptions
                    {
                        Name = "get_appointments",
                        Description = "Get scheduled appointments for a date range with optional filtering by risk level. Dates should be in ISO format (YYYY-MM-DD)."
                    }),

                AIFunctionFactory.Create(
                    predictionTool.GetPredictions,
                    new AIFunctionFactoryOptions
                    {
                        Name = "get_predictions",
                        Description = "Get no-show probability predictions for specific appointment IDs. Returns probability 0-1 and risk level."
                    })
            ]
        };
    }

    public async Task<ChatResponse> ChatAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        try
        {
            // Build the full message list including system prompt
            var fullMessages = new List<ChatMessage>
            {
                new(ChatRole.System, SystemPrompt)
            };
            fullMessages.AddRange(messages);

            _logger.LogInformation("Processing chat request with {MessageCount} messages", fullMessages.Count);

            // Use GetResponseAsync with options for tool support
            var response = await _chatClient.GetResponseAsync(fullMessages, _chatOptions, cancellationToken);

            _logger.LogInformation("Received response with {MessageCount} result messages", response.Messages.Count);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chat request");
            throw;
        }
    }
}
