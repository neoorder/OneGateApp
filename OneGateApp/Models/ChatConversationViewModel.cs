using Meshline.Models.Client;
using NeoOrder.OneGate.Properties;
using System.Globalization;

namespace NeoOrder.OneGate.Models;

public enum ChatConversationFilter { All, Unread }

public sealed record ChatConversationViewModel(string LocalAccountId, string Address, string? DisplayName,
    string? Text, DateTimeOffset UpdatedAt, long UnreadCount, string? Draft, bool HasMessages)
{
    public string Title => DisplayName ?? Address;

    // Keep message time separately so clearing a draft restores the original conversation position.
    public DateTimeOffset LastMessageAt { get; init; } = HasMessages ? UpdatedAt : DateTimeOffset.MinValue;
    public bool IsPinned { get; init; }
    public string PinActionText => IsPinned ? Strings.ChatUnpinConversation : Strings.ChatPinConversation;
    public MessageSendState? SendErrorState { get; init; }
    public bool HasSendError => SendErrorState is MessageSendState.Failed or MessageSendState.Canceled;
    public bool ShowSendError => HasSendError && string.IsNullOrWhiteSpace(Draft);
    public bool HasUnread => UnreadCount > 0;
    public bool HasDraft => !string.IsNullOrWhiteSpace(Draft);
    public bool MatchesFilter(ChatConversationFilter filter) => (HasMessages || HasDraft || IsPinned) && filter switch
    {
        ChatConversationFilter.All => true,
        ChatConversationFilter.Unread => HasUnread,
        _ => false
    };
    public string Preview => !string.IsNullOrWhiteSpace(Draft)
        ? $"{Strings.ChatDraft} {Draft.Replace("\r", " ").Replace("\n", " ")}"
        : ShowSendError ? $"[{(SendErrorState == MessageSendState.Canceled ? Strings.OperationCancelled : Strings.ChatMessageFailed)}] {MessagePreview}" : MessagePreview;
    string MessagePreview => !HasMessages ? Strings.ChatHistoryEmpty : string.IsNullOrEmpty(Text) ? Strings.ChatNonTextMessage
        : Text.Replace("\r", " ").Replace("\n", " ");
    // Match content itself, not the localized status or draft labels shown in the preview.
    public bool MatchesSearch(string filter) => filter.Length == 0
        || DisplayName?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true
        || Address.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Text?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true
        || Draft?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;

    public string TimeText => !HasMessages && !HasDraft ? "" : UpdatedAt.ToLocalTime().ToString(
        UpdatedAt.LocalDateTime.Date == DateTime.Today ? "t" : "d", CultureInfo.CurrentCulture);
}
