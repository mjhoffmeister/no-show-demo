namespace NoShowPredictor.Web.Models;

/// <summary>
/// Represents the role of a message in a chat conversation.
/// </summary>
public enum ChatRole
{
    User,
    Assistant,
    System
}

/// <summary>
/// Represents a single message in a chat conversation.
/// Used for display in the chat UI.
/// </summary>
public sealed class ChatMessageModel
{
    /// <summary>Unique message identifier</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Role of the message sender</summary>
    public ChatRole Role { get; set; }

    /// <summary>Message content (may contain markdown)</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>When the message was sent/received</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>Whether the message is waiting for a response (assistant typing)</summary>
    public bool IsLoading { get; set; }

    /// <summary>Optional error message if something went wrong</summary>
    public string? Error { get; set; }

    /// <summary>Referenced appointment IDs in the message</summary>
    public IReadOnlyList<int> ReferencedAppointmentIds { get; set; } = [];

    /// <summary>Helper property to check if this is a user message</summary>
    public bool IsUser => Role == ChatRole.User;

    /// <summary>Helper property to check if this is an assistant message</summary>
    public bool IsAssistant => Role == ChatRole.Assistant;

    /// <summary>Helper property to check if message has an error</summary>
    public bool HasError => !string.IsNullOrEmpty(Error);
}
