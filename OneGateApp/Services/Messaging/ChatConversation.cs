using Meshline.Models.Client;

namespace NeoOrder.OneGate.Services.Messaging;

public sealed record ChatConversation(string Address, string? Text, DateTimeOffset UpdatedAt,
    long UnreadCount, string? Draft, bool HasMessages)
{
    // Keep message time separately so clearing a draft restores the original conversation position.
    public DateTimeOffset LastMessageAt { get; init; } = HasMessages ? UpdatedAt : DateTimeOffset.MinValue;
    public bool IsPinned { get; init; }
    public MessageSendState? SendErrorState { get; init; }
}
