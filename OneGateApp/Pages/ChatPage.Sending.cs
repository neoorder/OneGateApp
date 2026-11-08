using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;

namespace NeoOrder.OneGate.Pages;

public partial class ChatPage
{
    (string AccountId, string Address)? draftConversation;
    bool editingFailedMessage;
    bool draftEdited;
    CancellationTokenSource? draftRetryCancellation;

    public string Draft
    {
        get;
        set
        {
            if (field == value) return;
            field = value ?? "";
            SaveDraft();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSend));
        }
    } = "";
    void SaveDraft()
    {
        if (draftConversation is not { } conversation) return;
        draftEdited = true;
        messagingService.SetChatDraft(conversation.AccountId, conversation.Address, Draft, replyTo);
        if (UseConversationPane) FilterConversations();
    }

    // The current draft must be restored before typing; message history can load independently.
    public bool CanCompose => IsChatEnabled && HasChatContact
        && draftConversation is { } conversation && conversation == (localAccountId, Address);
    public bool CanSend => CanCompose && !string.IsNullOrWhiteSpace(Draft);
    public string? SendErrorText { get; private set { field = value; OnPropertyChanged(); } }

    public string? DraftStorageNotice => IsChatEnabled && localAccountId == messagingService.AccountId
        && localAccountId is not null && messagingService.HasDraftStorageError(localAccountId)
        ? Strings.OperationFailed : null;

    public bool CanRetryDraftStorage => CanCompose && draftRetryCancellation is null && DraftStorageNotice is not null;

    void OnDraftStorageChanged(object? sender, EventArgs e)
    {
        // Recovery may also come from a background read. Never replace text the user has edited.
        if (!draftEdited && IsCurrentMessageContext(walletProvider.GetWallet(), localAccountId, Address)
            && draftConversation is { } conversation && conversation == (localAccountId, Address))
        {
            var restored = messagingService.GetChatDraft(conversation.AccountId, conversation.Address);
            // Another conversation's damaged draft must not hide this conversation's recovered content.
            if ((Draft != restored.Text || replyTo != restored.ReplyTo)
                && (restored.Text.Length > 0 || restored.ReplyTo is not null || !messagingService.HasDraftStorageError(conversation.AccountId)))
            {
                RestoreDraft(force: true);
                FilterConversations();
            }
        }
        OnPropertyChanged(nameof(DraftStorageNotice));
        OnPropertyChanged(nameof(CanRetryDraftStorage));
    }

    async void OnRetryDraftStorageClicked(object sender, EventArgs e)
    {
        var wallet = walletProvider.GetWallet();
        string? accountId = localAccountId;
        string address = Address;
        if (!CanRetryDraftStorage || !IsCurrentMessageContext(wallet, accountId, address)) return;
        using var cancellation = new CancellationTokenSource();
        draftRetryCancellation = cancellation;
        OnPropertyChanged(nameof(CanRetryDraftStorage));
        try
        {
            await messagingService.RetryChatDraftStorageAsync(accountId!, cancellation.Token);
            if (!cancellation.IsCancellationRequested && IsCurrentMessageContext(wallet, accountId, address))
                OnDraftStorageChanged(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Meshline] Draft storage retry failed: {ex}");
            if (!cancellation.IsCancellationRequested && IsCurrentMessageContext(wallet, accountId, address)) SendErrorText = Strings.OperationFailed;
        }
        finally
        {
            if (ReferenceEquals(draftRetryCancellation, cancellation)) draftRetryCancellation = null;
            OnPropertyChanged(nameof(CanRetryDraftStorage));
        }
    }

    void RestoreDraft(bool force = false)
    {
        if (localAccountId is null) return;
        var conversation = (AccountId: localAccountId, Address);
        if (!force && draftConversation == conversation) return;
        // Background message refreshes must not replace text being composed.
        draftConversation = null;
        var restored = messagingService.GetChatDraft(conversation.AccountId, conversation.Address);
        Draft = restored.Text;
        SetReplyDraft(restored.ReplyTo);
        draftConversation = conversation;
        draftEdited = false;
        MoveDraftCursorToEnd();
    }

    void MoveDraftCursorToEnd()
    {
        messageEditor.SelectionLength = 0;
        messageEditor.CursorPosition = Draft.Length;
    }

    void DetachDraft()
    {
        // Clear the editor without erasing the stored draft for the previous conversation.
        draftRetryCancellation?.Cancel();
        draftRetryCancellation = null;
        _ = messagingService.FlushChatDraftsAsync();
        draftConversation = null;
        Draft = "";
        SetReplyDraft(null);
    }

    void OnSendClicked(object sender, EventArgs e)
    {
        if (!CanSend || localAccountId is not { } accountId || switchingConversation
            || !IsCurrentMessageContext(walletProvider.GetWallet(), accountId, Address)) return;
        try
        {
            bool scrollToEnd = QueueMessageForCurrentChat(() => messagingService.QueueTextMessage(accountId, Address, Draft, replyTo));
            SetReplyDraft(null);
            Draft = "";
            SendErrorText = null;
            UpdateAfterSend(scrollToEnd);
#if WINDOWS
            messageEditor.Focus();
#endif
        }
        catch (Exception ex) { SendErrorText = ex.Message; }
    }

    bool QueueMessageForCurrentChat(Action queue)
    {
        // Queueing can synchronously refresh the list. Use the viewport from before the insertion.
        bool scrollToEnd = Messages.Count == 0 || IsLastMessageVisible();
        bool wasFollowing = followLatest;
        followLatest = false;
        try { queue(); }
        catch { followLatest = wasFollowing; throw; }
        return scrollToEnd;
    }

    void UpdateAfterSend(bool scrollToEnd)
    {
        followLatest = false;
        RefreshPendingMessages();
        followLatest = scrollToEnd;
        if (!scrollToEnd || Messages.LastOrDefault() is not { } target)
        {
            NotifyScrollState();
            return;
        }

        CancelQuoteNavigation();
        ClearQuoteHighlight();
        ClearQuoteReturnPosition();
        RequestBottomScroll(target);
    }

    void OnRetrySendClicked(object sender, EventArgs e)
    {
        if (GetMessageActionParameter(sender) is { } message) RetryMessage(message);
    }

    void RetryMessage(ChatMessageViewModel message)
    {
        if (!CanCompose || !IsCurrentMessageContext(walletProvider.GetWallet(), localAccountId, Address)
            || !Messages.Contains(message) || message is not { CanRetry: true, PendingId: { } id }) return;
        try
        {
            messagingService.RetryPendingMessage(id);
            SendErrorText = null;
            RefreshPendingMessages();
        }
        catch (Exception ex)
        {
            SendErrorText = ex.Message;
        }
    }

    async void OnEditFailedMessageClicked(object sender, EventArgs e)
    {
        if (GetMessageActionParameter(sender) is { } message) await EditFailedMessageAsync(message);
    }

    async Task EditFailedMessageAsync(ChatMessageViewModel message)
    {
        if (editingFailedMessage || !CanCompose || !IsCurrentMessageContext(walletProvider.GetWallet(), localAccountId, Address)
            || !message.CanEditForResend || !Messages.Contains(message)) return;
        editingFailedMessage = true;
        var wallet = walletProvider.GetWallet();
        string? accountId = localAccountId;
        string address = Address;
        string previousDraft = Draft;
        var previousReply = replyTo;
        string text = message.CopyText!;
        try
        {
            if ((previousDraft.Length > 0 || previousReply is not null)
                && (previousDraft != text || previousReply != message.ReplyTo)
                && !await DisplayAlertAsync(Strings.ChatEditFailedMessage, Strings.ChatReplaceDraftPrompt,
                    Strings.ChatReplaceDraft, Strings.Cancel)) return;
            // A reply, navigation, or another edit while the dialog was open must not overwrite a new draft.
            if (!CanCompose || !IsCurrentMessageContext(wallet, accountId, address)
                || Draft != previousDraft || replyTo != previousReply || !Messages.Contains(message) || !message.CanEditForResend) return;
            SetReplyDraft(message.ReplyTo);
            Draft = text;
            SendErrorText = null;
            messageEditor.Focus();
            MoveDraftCursorToEnd();
        }
        catch (Exception ex)
        {
            if (IsCurrentMessageContext(wallet, accountId, address))
            {
                SendErrorText = ex.Message;
            }
        }
        finally { editingFailedMessage = false; }
    }

    void RefreshPendingMessages()
    {
        if (!IsChatEnabled || localAccountId is null) return;
        var saved = Messages.Where(message => !message.IsPending).ToArray();
        if (SetMessagesWithPending(saved, appendNewRows: IsMessageListPresented)
            && followLatest && Messages.LastOrDefault() is { } target)
            DispatchMessageScroll(target, scrollToEnd: true);
    }

    bool SetMessagesWithPending(ChatMessageViewModel[] saved, HashSet<string>? submittedMessageIds = null, bool appendNewRows = false)
    {
        var savedIds = saved.Where(message => message.IsOutgoing).Select(message => message.MessageId!).ToHashSet(StringComparer.Ordinal);
        if (submittedMessageIds is not null) savedIds.UnionWith(submittedMessageIds);
        var (confirmed, remaining) = messagingService.ReconcilePendingMessages(localAccountId!, Address, savedIds);
        bool confirmedInPlace = false;
        foreach (var message in confirmed)
        {
            if (Messages.FirstOrDefault(row => row.IsPending && row.PendingId == message.Id) is not { } viewModel) continue;
            var status = message.SendStatus!;
            int index = Array.FindIndex(saved, row => row.IsOutgoing && row.MessageId == status.MessageId);
            if (index < 0) continue;

            viewModel.ConfirmPending(saved[index].SavedMessage!, status);
            saved[index] = viewModel;
            confirmedInPlace = true;
        }

        var pending = remaining.Select(message =>
        {
            var viewModel = Messages.FirstOrDefault(row => row.IsPending && row.PendingId == message.Id) ?? new ChatMessageViewModel(message);
            viewModel.UpdatePending(message);
            return viewModel;
        }).ToArray();
        if (appendNewRows) return SetMessages([.. pending, .. saved], refreshExisting: confirmedInPlace, appendNewRows: true);

        // Choose insertion positions for new rows using SDK order and local creation times.
        ChatMessageViewModel[] updated = new ChatMessageViewModel[saved.Length + pending.Length];
        int savedIndex = 0, pendingIndex = 0;
        for (int i = 0; i < updated.Length; i++)
            updated[i] = savedIndex < saved.Length
                && (pendingIndex == pending.Length || saved[savedIndex].Timestamp <= pending[pendingIndex].Timestamp)
                ? saved[savedIndex++] : pending[pendingIndex++];
        if (confirmedInPlace || !Messages.SequenceEqual(updated)) return SetMessages(updated, refreshExisting: confirmedInPlace);
        return false;
    }

    async Task ReadSendStatusesAsync(CancellationToken cancellationToken)
    {
        if (!Messages.Any(message => message.IsOutgoing && !message.IsPending)) return;
        var statuses = await messagingService.GetChatSendStatusesAsync(localAccountId!, Address, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var message in Messages.Where(message => message.IsOutgoing && !message.IsPending))
            message.UpdateSendStatus(statuses.GetValueOrDefault(message.MessageId!));
    }
}
