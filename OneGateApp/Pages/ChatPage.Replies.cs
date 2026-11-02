using Meshline.Models.Client;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;

namespace NeoOrder.OneGate.Pages;

public partial class ChatPage
{
    // Immutable originals live only as long as this conversation. Missing rows retry on demand.
    readonly Dictionary<MessageRef, (MessageInfo? Message, DateTimeOffset RetryAfter)> replyMessages = [];

    void UpdateReplyPreviews()
    {
        if (localAccountId is null) return;
        var originals = Messages.Where(value => value.SavedMessage is not null)
            .ToDictionary(value => value.SavedMessage!.Key, value => value.SavedMessage!);
        foreach (var reference in GetReplyReferences())
            if (originals.TryGetValue(reference, out var original))
                replyMessages[reference] = (original, DateTimeOffset.MaxValue);
        foreach (var message in Messages.Where(value => value.HasReply))
            message.UpdateReplyPreview(replyMessages.GetValueOrDefault(message.ReplyTo!).Message, localAccountId, DisplayName);
        UpdateReplyDraftPreview();
    }

    async Task ReadReplyPreviewsAsync(CancellationToken cancellationToken)
    {
        var wallet = walletProvider.GetWallet();
        string? accountId = localAccountId;
        string address = Address;
        if (!IsCurrentMessageContext(wallet, accountId, address)) return;
        UpdateReplyPreviews();
        var now = DateTimeOffset.UtcNow;
        var missing = GetReplyReferences()
            .Where(reference => !replyMessages.TryGetValue(reference, out var cached) || now >= cached.RetryAfter).ToArray();
        if (missing.Length == 0) return;
        // A failed local read leaves normal messages usable and does not trigger a query on every notification.
        foreach (var reference in missing) replyMessages[reference] = (null, now.AddMinutes(1));
        try
        {
            var originals = await messagingService.GetChatReplyMessagesAsync(accountId!, address, missing, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentMessageContext(wallet, accountId, address)) return;
            foreach (var (reference, original) in originals)
                replyMessages[reference] = (original, DateTimeOffset.MaxValue);
            UpdateReplyPreviews();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // The bubble already has an unavailable placeholder. Retry only when the page next needs it.
        }
    }

    MessageRef? replyTo;

    public bool HasReplyDraft => replyTo is not null;
    public string ReplyDraftTitle { get; private set; } = Strings.ChatQuotedMessage;
    public string ReplyDraftPreview { get; private set; } = Strings.ChatQuotedMessageUnavailable;

    void OnReplyMessageClicked(object sender, EventArgs e)
    {
        if (GetMessageActionParameter(sender) is { } message) BeginReply(message);
    }

    void BeginReply(ChatMessageViewModel message)
    {
        if (!CanCompose || switchingConversation || !IsCurrentMessageContext(walletProvider.GetWallet(), localAccountId, Address)
            || !Messages.Contains(message) || !message.CanReply || message.SavedMessage is not { } original) return;
        ApplyReplyDraft(original);
    }

    void ApplyReplyDraft(MessageInfo original)
    {
        replyMessages[original.Key] = (original, DateTimeOffset.MaxValue);
        SetReplyDraft(original.Key);
        SendErrorText = null;
        messageEditor.Focus();
        MoveDraftCursorToEnd();
    }

    void OnCancelReplyClicked(object sender, EventArgs e)
    {
        if (!CanCompose || !IsCurrentMessageContext(walletProvider.GetWallet(), localAccountId, Address)) return;
        SetReplyDraft(null);
    }

    void SetReplyDraft(MessageRef? reference)
    {
        if (replyTo == reference) return;
        replyTo = reference;
        SaveDraft();
        UpdateReplyPreviews();
        OnPropertyChanged(nameof(HasReplyDraft));
    }

    IEnumerable<MessageRef> GetReplyReferences() => Messages.Where(message => message.HasReply)
        .Select(message => message.ReplyTo!).Concat(replyTo is null ? [] : new[] { replyTo }).Distinct();

    void UpdateReplyDraftPreview()
    {
        var original = replyTo is null ? null : replyMessages.GetValueOrDefault(replyTo).Message;
        string title = original is null ? Strings.ChatQuotedMessage
            : string.Format(Strings.ChatQuotedMessageFrom, original.Key.Sender == localAccountId ? Strings.ChatYou : DisplayName);
        string text = ChatMessageViewModel.FormatQuotePreview(original);
        if (ReplyDraftTitle != title)
        {
            ReplyDraftTitle = title;
            OnPropertyChanged(nameof(ReplyDraftTitle));
        }
        if (ReplyDraftPreview != text)
        {
            ReplyDraftPreview = text;
            OnPropertyChanged(nameof(ReplyDraftPreview));
        }
    }
}
