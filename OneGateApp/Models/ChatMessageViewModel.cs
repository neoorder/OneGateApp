using Meshline.Models.Client;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services.Messaging;
using System.ComponentModel;
using System.Globalization;

namespace NeoOrder.OneGate.Models;

public sealed class ChatMessageViewModel : INotifyPropertyChanged
{
    readonly MessageInfo? message;
    PendingChatMessage? pending;
    MessageSendState? sendState;
    string? sendError;

    public ChatMessageViewModel(MessageInfo message, string localAccountId)
    {
        this.message = message;
        IsOutgoing = message.Key.Sender == localAccountId;
    }

    public ChatMessageViewModel(PendingChatMessage pending)
    {
        this.pending = pending;
        sendState = pending.SendStatus?.State;
        sendError = pending.SendStatus?.ErrorMessage;
        IsOutgoing = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public Guid? PendingId => pending?.Id;
    public bool IsPending => pending is not null;
    public string? MessageId => message?.Key.MessageId ?? pending?.SendStatus?.MessageId;
    public long LocalSequence => message?.LocalSequence ?? 0;
    internal MessageInfo? SavedMessage => message;
    public MessageRef? ReplyTo => message?.ReplyTo ?? pending?.ReplyTo;
    public bool CanReply => message is not null && (!IsOutgoing
        || sendState is null or MessageSendState.RelayAccepted or MessageSendState.TargetAccepted);
    public bool CanShowActions => CanCopyText || CanReply;
    public bool HasReply => ReplyTo is not null;
    public bool IsQuoteHighlighted
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new(nameof(IsQuoteHighlighted)));
        }
    }
    public string ReplyTitle { get; private set; } = Strings.ChatQuotedMessage;
    public string ReplyPreview { get; private set; } = Strings.ChatQuotedMessageUnavailable;
    public bool IsOutgoing { get; }
    public string? SenderAccountId => message?.Key.Sender ?? pending?.LocalAccountId;
    public string? CopyText => pending?.Text ?? message?.Body?.Text;
    public bool CanCopyText => !string.IsNullOrEmpty(CopyText);
    public bool CanForwardText => CanCopyText && !HasAttachments
        && (pending is not null || message?.Body?.ContentType == "text/plain");
    public string Text => CanCopyText ? CopyText! : Strings.ChatNonTextMessage;
    public string BubbleText
    {
        get
        {
            string text = Text;
            int length = GetBubblePreviewLength(text);
            return length < text.Length ? text[..length] + "…" : text;
        }
    }
    public bool HasTruncatedText => GetBubblePreviewLength(Text) < Text.Length;
    public bool HasAttachments => message?.Attachments is { Count: > 0 };
    public DateTimeOffset Timestamp => pending?.CreatedAt ?? message!.CreatedAt;
    DateTimeOffset LocalCreatedAt => Timestamp.ToLocalTime();
    public DateOnly LocalDate => DateOnly.FromDateTime(LocalCreatedAt.DateTime);
    public string DateText => LocalDate.ToString("d", CultureInfo.CurrentCulture);
    public string CreatedAt => LocalCreatedAt.ToString("t", CultureInfo.CurrentCulture);
    public bool ShowDateSeparator
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new(nameof(ShowDateSeparator)));
        }
    }
    public string? ErrorText => HasSendError ? pending?.ErrorMessage ?? sendError : null;
    public bool CanRetry => pending is { ErrorMessage: not null, SendStatus: null };
    public bool CanEditForResend => HasSendError && CanCopyText && !HasAttachments
        && (pending is not null || message?.Body?.ContentType == "text/plain");
    public string? SendStatusText => pending?.IsCanceled == true ? Strings.OperationCancelled
        : pending?.IsSubmitting == true ? Strings.ChatMessageQueued
        : CanRetry ? Strings.ChatMessageFailed : !IsOutgoing ? null : sendState switch
    {
        MessageSendState.Queued => Strings.ChatMessageQueued,
        MessageSendState.Submitting => Strings.ChatMessageSending,
        MessageSendState.SubmissionUnknown => Strings.ChatMessageUnconfirmed,
        MessageSendState.RelayAccepted or MessageSendState.TargetAccepted => Strings.ChatMessageSent,
        MessageSendState.Failed => Strings.ChatMessageFailed,
        MessageSendState.Canceled => Strings.OperationCancelled,
        _ => Strings.ChatMessageStatusUnavailable
    };
    public MessageSendState? SendErrorState => pending?.IsCanceled == true ? MessageSendState.Canceled
        : CanRetry ? MessageSendState.Failed
        : IsOutgoing && sendState is (MessageSendState.Failed or MessageSendState.Canceled) ? sendState : null;
    public bool HasSendError => SendErrorState is not null;

    public void UpdateReplyPreview(MessageInfo? original, string localAccountId, string contactName)
    {
        if (ReplyTo is null) return;
        string? sender = message?.Key.Sender ?? pending?.LocalAccountId;
        string? recipient = message?.Recipient;
        if (pending is not null)
            recipient = pending.LocalAccountId[..(pending.LocalAccountId.LastIndexOf(':') + 1)] + pending.Address;
        bool matches = original is not null && original.Key == ReplyTo
            && (original.Key.Sender == sender && original.Recipient == recipient
                || original.Key.Sender == recipient && original.Recipient == sender);
        string title = matches ? string.Format(Strings.ChatQuotedMessageFrom,
            original!.Key.Sender == localAccountId ? Strings.ChatYou : contactName) : Strings.ChatQuotedMessage;
        string text = FormatQuotePreview(matches ? original : null);
        if (ReplyTitle != title)
        {
            ReplyTitle = title;
            PropertyChanged?.Invoke(this, new(nameof(ReplyTitle)));
        }
        if (ReplyPreview != text)
        {
            ReplyPreview = text;
            PropertyChanged?.Invoke(this, new(nameof(ReplyPreview)));
        }
    }

    internal static string FormatQuotePreview(MessageInfo? original)
    {
        if (original is null) return Strings.ChatQuotedMessageUnavailable;
        string text = string.IsNullOrWhiteSpace(original.Body?.Text) ? Strings.ChatNonTextMessage : original.Body.Text;
        // Keep a quote compact even when the original contains a very long body.
        if (text.Length <= 240) return text;
        int length = char.IsHighSurrogate(text[239]) ? 239 : 240;
        return text[..length] + "…";
    }

    static int GetBubblePreviewLength(string text)
    {
        // Roughly one phone screen at the default font: 20 CJK characters per line, 30 lines.
        const int maxLength = 600;
        const int maxLines = 30;
        int length = 0;
        int lines = 1;
        while (length < text.Length && length < maxLength)
        {
            // Keep emoji and combining marks intact; CRLF is a single text element.
            int next = length + StringInfo.GetNextTextElementLength(text, length);
            if (next > maxLength) break;
            if (text[length] is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029')
            {
                if (lines == maxLines) break;
                lines++;
            }
            length = next;
        }
        return length;
    }

    public void UpdatePending(PendingChatMessage value)
    {
        bool pendingChanged = pending?.ErrorMessage != value.ErrorMessage
            || (pending?.SendStatus is null) != (value.SendStatus is null)
            || pending?.IsCanceled != value.IsCanceled || pending?.HasSubmissionStarted != value.HasSubmissionStarted;
        pending = value;
        UpdateSendStatus(value.SendStatus, pendingChanged);
    }

    public void UpdateSendStatus(MessageSendStatus? status)
    {
        // The SDK may prune terminal outbox records; absence does not undo an observed result.
        if (status is null && sendState is (MessageSendState.RelayAccepted or MessageSendState.TargetAccepted
            or MessageSendState.Failed or MessageSendState.Canceled)) return;
        UpdateSendStatus(status, pendingChanged: false);
    }

    void UpdateSendStatus(MessageSendStatus? status, bool pendingChanged)
    {
        MessageSendState? state = status?.State;
        string? error = status?.ErrorMessage;
        if (!pendingChanged && sendState == state && sendError == error) return;
        sendState = state;
        sendError = error;
        PropertyChanged?.Invoke(this, new(nameof(SendStatusText)));
        PropertyChanged?.Invoke(this, new(nameof(HasSendError)));
        PropertyChanged?.Invoke(this, new(nameof(ErrorText)));
        if (pendingChanged) PropertyChanged?.Invoke(this, new(nameof(CanRetry)));
        PropertyChanged?.Invoke(this, new(nameof(CanEditForResend)));
        PropertyChanged?.Invoke(this, new(nameof(CanReply)));
        PropertyChanged?.Invoke(this, new(nameof(CanShowActions)));
    }
}
