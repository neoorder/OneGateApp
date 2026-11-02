using CommunityToolkit.Maui.Alerts;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;

namespace NeoOrder.OneGate.Pages;

public partial class ChatPage
{
    bool forwardingMessage;
    Entry? forwardRecipientSearchEntry;
    Editor? forwardMessageEditor;
    TaskCompletionSource? forwardRecipientSelection;
    Action<ChatForwardRecipient>? queueForwardMessage;
    Func<bool>? forwardRecipientContext;
    ChatForwardRecipient[] allForwardRecipients = [];

    async void OnForwardMessageClicked(object sender, EventArgs e)
    {
        if (!showingMessageActions && GetMessageActionParameter(sender) is { } message)
            await ForwardMessageAsync(message);
    }

    async Task ForwardMessageAsync(ChatMessageViewModel message)
    {
        var wallet = walletProvider.GetWallet();
        string? accountId = localAccountId;
        string address = Address;
        int searchRevision = messageSearchViewRevision;
        int readerRevision = messageReaderRevision;
        bool IsCurrent() => HasLoaded && !switchingConversation && !ShowMessageReader && !ShowMessageSearch
            && searchRevision == messageSearchViewRevision && readerRevision == messageReaderRevision
            && IsCurrentMessageContext(wallet, accountId, address);
        if (!IsCurrent() || !Messages.Contains(message) || !message.CanForwardText) return;
        // Capture only the text. A quote reference belongs to the source conversation.
        await ForwardMessageTextAsync(message.CopyText!, accountId!, IsCurrent, error => ErrorText = error);
    }

    async Task ForwardMessageTextAsync(string text, string accountId, Func<bool> isCurrent, Action<string?> setError)
    {
        if (forwardingMessage || openingMessageLink || sharingMessageText || switchingConversation || !isCurrent()) return;
        forwardingMessage = true;
        setError(null);
        try
        {
            var snapshot = await messagingService.GetChatRecipientsAsync();
            if (!isCurrent() || snapshot.LocalAccountId != accountId) return;
            var labels = conversationLabels ?? (await addressBookService.GetEntriesAsync())
                .ToDictionary(contact => contact.Address, contact => contact.Label, StringComparer.Ordinal);
            if (!isCurrent()) return;
            conversationLabels = labels;
            var targets = snapshot.Contacts
                .Select(recipient =>
                {
                    string? label = labels.GetValueOrDefault(recipient);
                    return new ChatForwardRecipient(recipient,
                        messagingService.GetChatDisplayName(accountId, recipient, label), label);
                }).ToArray();
            if (targets.Length == 0)
            {
                setError(Strings.ChatForwardNoContacts);
                return;
            }
            await PickForwardRecipientAsync(targets, text, isCurrent,
                selected => messagingService.QueueTextMessage(accountId, selected.Address, text));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (isCurrent()) setError(ex.Message); }
        finally { forwardingMessage = false; }
    }

    public string? ForwardRecipientError { get; private set { field = value; OnPropertyChanged(); } }
    public bool ShowForwardRecipientPicker => forwardRecipientSelection is not null;
    public ChatForwardRecipient[] ForwardRecipients { get; private set; } = [];
    public ChatForwardRecipient? ForwardRecipientPreview
    {
        get;
        private set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowForwardRecipientPreview)); }
    }
    public bool ShowForwardRecipientPreview => ForwardRecipientPreview is not null;
    public string? ForwardTextPreview { get; private set { field = value; OnPropertyChanged(); } }
    public string ForwardRecipientSearchText
    {
        get;
        set
        {
            field = value ?? "";
            OnPropertyChanged();
            FilterForwardRecipients();
        }
    } = "";

    void FilterForwardRecipients()
    {
        string filter = ForwardRecipientSearchText.Trim();
        var recipients = allForwardRecipients.Where(contact => contact.MatchesSearch(filter))
            .OrderByDescending(contact => contact.Address == Address)
            .ThenBy(contact => contact.DisplayName ?? contact.Address, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(contact => contact.Address, StringComparer.Ordinal).ToArray();
        if (ForwardRecipients.SequenceEqual(recipients)) return;
        ForwardRecipients = recipients;
        OnPropertyChanged(nameof(ForwardRecipients));
    }

    void RefreshForwardRecipientNames()
    {
        if (!ShowForwardRecipientPicker || localAccountId is null || forwardRecipientContext?.Invoke() != true) return;
        bool changed = false;
        foreach (var contact in allForwardRecipients)
            changed |= contact.UpdateDisplayName(messagingService.GetChatDisplayName(
                localAccountId, contact.Address, contact.Label));
        if (changed) FilterForwardRecipients();
    }

    Task PickForwardRecipientAsync(ChatForwardRecipient[] contacts, string text, Func<bool> isCurrent,
        Action<ChatForwardRecipient> queue)
    {
        if (ShowForwardRecipientPicker || !isCurrent()) return Task.CompletedTask;
        var view = EnsureChatView(forwardRecipientView);
        forwardRecipientSearchEntry ??= view.FindByName<Entry>("forwardRecipientSearchEntry");
        forwardMessageEditor ??= view.FindByName<Editor>("forwardMessageEditor");
        var selection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        forwardRecipientSelection = selection;
        queueForwardMessage = queue;
        ForwardRecipientError = null;
        forwardRecipientContext = isCurrent;
        allForwardRecipients = contacts;
        ForwardRecipientPreview = null;
        ForwardTextPreview = text;
        ForwardRecipientSearchText = "";
        // A profile may have completed while the forwarding targets were being assembled.
        RefreshForwardRecipientNames();
        messageEditor.Unfocus();
        messageSearchEntry?.Unfocus();
        NotifyForwardRecipientPicker();
#if WINDOWS
        Dispatcher.Dispatch(() =>
        {
            if (ReferenceEquals(forwardRecipientSelection, selection) && !ShowForwardRecipientPreview && isCurrent())
                forwardRecipientSearchEntry?.Focus();
        });
#endif
        return selection.Task;
    }

    void OnForwardRecipientSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!ShowForwardRecipientPicker || ShowForwardRecipientPreview) return;
        if (forwardRecipientContext?.Invoke() != true)
        {
            CloseForwardRecipientPicker(resumeChat: false);
            return;
        }
        if (e.CurrentSelection.FirstOrDefault() is ChatForwardRecipient contact
            && ForwardRecipients.Any(candidate => ReferenceEquals(candidate, contact)))
        {
            // Clear native selection so returning to the list allows choosing the same contact again.
            if (sender is CollectionView list) list.SelectedItem = null;
            forwardRecipientSearchEntry?.Unfocus();
            ForwardRecipientPreview = contact;
            var selection = forwardRecipientSelection;
            Dispatcher.Dispatch(() =>
            {
                if (ReferenceEquals(forwardRecipientSelection, selection) && ReferenceEquals(ForwardRecipientPreview, contact)
                    && forwardRecipientContext?.Invoke() == true)
                    ResetMessageTextPosition(forwardMessageEditor!);
            });
        }
    }

    async void OnSendForwardMessageClicked(object sender, EventArgs e)
    {
        if (forwardRecipientSelection is not { } selection) return;
        if (forwardRecipientContext?.Invoke() != true)
        {
            CloseForwardRecipientPicker(resumeChat: false);
            return;
        }
        if (ForwardRecipientPreview is not { } contact || queueForwardMessage is not { } queue
            || !allForwardRecipients.Any(candidate => ReferenceEquals(candidate, contact))) return;
        ForwardRecipientError = null;
        try
        {
            // Use the same optimistic send queue as the composer, without touching either draft.
            // Contact validation and delivery run in the queue; failures remain retryable in the target chat.
            queue(contact);
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(forwardRecipientSelection, selection)) ForwardRecipientError = ex.Message;
            return;
        }
        if (!ReferenceEquals(forwardRecipientSelection, selection)) return;
        bool showForwardedMessage = contact.Address == Address && !ShowMessageSearch;
        CloseForwardRecipientPicker(resumeChat: !showForwardedMessage);
        if (showForwardedMessage)
        {
            ShowLatestPendingMessage();
            QueueLatestMessageScroll();
        }
        else await Toast.Show(Strings.ChatMessageQueued);
    }

    void BackFromForwardRecipientPicker()
    {
        ForwardRecipientError = null;
        if (!ShowForwardRecipientPreview) CloseForwardRecipientPicker();
        else if (forwardRecipientContext?.Invoke() != true) CloseForwardRecipientPicker(resumeChat: false);
        else ForwardRecipientPreview = null;
    }

    void OnCancelForwardRecipientClicked(object sender, EventArgs e)
    {
        CloseForwardRecipientPicker();
    }

    void CloseForwardRecipientPicker(bool resumeChat = true)
    {
        if (forwardRecipientSelection is not { } selection) return;
        bool wasCurrent = forwardRecipientContext?.Invoke() == true;
        forwardRecipientSelection = null;
        forwardRecipientContext = null;
        queueForwardMessage = null;
        ForwardRecipientError = null;
        allForwardRecipients = [];
        ForwardRecipientPreview = null;
        ForwardTextPreview = null;
        forwardRecipientSearchEntry?.Unfocus();
        ForwardRecipientSearchText = "";
        NotifyForwardRecipientPicker();
        selection.TrySetResult();
        if (resumeChat && wasCurrent && !IsChatContentCovered && HasLoaded && followLatest && !switchingConversation)
        {
            // Messages arriving under the picker still require confirmation from the visible native viewport.
            QueueLatestMessageScroll();
        }
    }

    void NotifyForwardRecipientPicker()
    {
        OnPropertyChanged(nameof(ShowForwardRecipientPicker));
        OnPropertyChanged(nameof(IsChatContentCovered));
        OnPropertyChanged(nameof(CanOpenMessageSearch));
        OnPropertyChanged(nameof(CanOpenContactDetails));
    }
}
