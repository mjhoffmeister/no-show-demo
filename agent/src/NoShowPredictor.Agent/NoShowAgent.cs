using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NoShowPredictor.Agent.Data;
using NoShowPredictor.Agent.Services;
using NoShowPredictor.Agent.Tools;

namespace NoShowPredictor.Agent;

/// <summary>
/// No-Show Predictor Agent - helps scheduling coordinators identify high-risk appointments.
/// </summary>
public static class NoShowAgent
{
    /// <summary>
    /// System prompt defining the agent's personality and capabilities.
    /// </summary>
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
        You have access to:
        - Appointment schedules (past and future)
        - Patient demographics and history
        - Provider schedules
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

    /// <summary>
    /// Creates and configures the No-Show Agent with all necessary tools.
    /// </summary>
    public static AIAgent Create(
        IChatClient chatClient,
        IAppointmentRepository appointmentRepository,
        IMLEndpointClient mlEndpointClient,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(appointmentRepository);
        ArgumentNullException.ThrowIfNull(mlEndpointClient);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var tools = CreateTools(appointmentRepository, mlEndpointClient, loggerFactory);

        // Build the agent with tools and OpenTelemetry instrumentation
        var agent = new ChatClientAgent(
                chatClient,
                instructions: SystemPrompt,
                tools: tools)
            .AsBuilder()
            .UseOpenTelemetry(sourceName: "NoShowAgent")
            .Build();

        return agent;
    }

    /// <summary>
    /// Creates the tool definitions for the agent.
    /// </summary>
    private static List<AITool> CreateTools(
        IAppointmentRepository appointmentRepository,
        IMLEndpointClient mlEndpointClient,
        ILoggerFactory loggerFactory)
    {
        // Create tool instances
        var appointmentTool = new AppointmentTool(
            appointmentRepository,
            loggerFactory.CreateLogger<AppointmentTool>());

        var predictionTool = new PredictionTool(
            appointmentRepository,
            mlEndpointClient,
            loggerFactory.CreateLogger<PredictionTool>());

        // Register tools as AI functions
        return
        [
            AIFunctionFactory.Create(
                appointmentTool.GetAppointmentsByDateRange,
                new AIFunctionFactoryOptions
                {
                    Name = "get_appointments",
                    Description = "Get scheduled appointments for a date range with optional filtering by risk level"
                }),

            AIFunctionFactory.Create(
                predictionTool.GetPredictions,
                new AIFunctionFactoryOptions
                {
                    Name = "get_predictions",
                    Description = "Get no-show probability predictions for specific appointment IDs"
                })
        ];
    }
}
