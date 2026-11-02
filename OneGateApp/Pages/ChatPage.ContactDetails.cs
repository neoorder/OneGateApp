using Neo.Wallets;
using NeoOrder.OneGate.Models;

namespace NeoOrder.OneGate.Pages;

public partial class ChatPage
{
    ContactDetailsHistory? contactDetailsHistory;

    sealed record ContactDetailsHistory(Wallet Wallet, WalletAccount Account, string AccountId, string Address,
        ChatMessageViewModel[] Messages, int FirstVisibleIndex, bool FollowLatest, bool HasMore,
        int NewMessageCount, long? FirstNewMessageSequence);

    ContactDetailsHistory? CaptureContactDetailsHistory()
    {
        var wallet = walletProvider.GetWallet();
        if (!HasLoaded || localAccountId is null
            || wallet?.GetDefaultAccount() is not { } account
            || !IsCurrentMessageContext(wallet, localAccountId, Address)) return null;
        // Pending bubbles are rebuilt from the current queue when returning.
        var saved = Messages.Where(message => !message.IsPending).ToArray();
        if (saved.Length == 0) return null;
        var anchor = Messages.Take(firstVisibleIndex + 1).LastOrDefault(message => !message.IsPending);
        int index = Math.Max(0, Array.IndexOf(saved, anchor));
        return new(wallet, account, localAccountId, Address, saved, index, followLatest, HasMore, newMessageCount, firstNewMessageSequence);
    }

    void RestoreContactDetailsHistory()
    {
        var history = contactDetailsHistory;
        contactDetailsHistory = null;
        if (history is null || !IsCurrentMessageContext(history.Wallet, history.AccountId, history.Address)
            || !ReferenceEquals(history.Account, history.Wallet.GetDefaultAccount())) return;
        followLatest = history.FollowLatest;
        firstVisibleIndex = history.FirstVisibleIndex;
        HasLoaded = true;
        HasMore = history.HasMore;
        newMessageCount = history.NewMessageCount;
        firstNewMessageSequence = history.FirstNewMessageSequence;
        ResetMessagePresentation();
        initialScrollAnchor = followLatest ? null : history.Messages[firstVisibleIndex];
        SetMessages(history.Messages);
    }

    public ContactViewModel? ChatContact { get; private set; }

    void OnMessageAvatarTapped(object sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: ChatMessageViewModel { IsOutgoing: false } })
            OnContactDetailsClicked(sender, e);
    }

    async void OnContactDetailsClicked(object sender, EventArgs e)
    {
        if (openingDetails || ChatContact is not { } contact) return;
        string? accountId = localAccountId;
        var wallet = walletProvider.GetWallet();
        if (!IsCurrentContact(contact, accountId, wallet)) return;
        openingDetails = true;
        contactDetailsHistory = CaptureContactDetailsHistory();
        try
        {
            await Shell.Current.GoToAsync("contact-details", new ShellNavigationQueryParameters { ["contact"] = contact });
        }
        catch (Exception ex) { if (IsCurrentContact(contact, accountId, wallet)) ErrorText = ex.Message; }
        finally
        {
            if (isVisible) contactDetailsHistory = null;
            openingDetails = false;
        }
    }

    async void OnRequestChatClicked(object sender, EventArgs e)
    {
        if (openingDetails || !ShowContactRequired || ChatContact is not { } contact
            || !IsCurrentMessageContext(walletProvider.GetWallet(), localAccountId, Address)) return;
        openingDetails = true;
        contactDetailsHistory = CaptureContactDetailsHistory();
        try { await Shell.Current.GoToAsync("chat-request", new ShellNavigationQueryParameters { ["address"] = contact.Address }); }
        catch (Exception ex)
        {
            if (isVisible && Address == contact.Address) ErrorText = ex.Message;
        }
        finally
        {
            if (isVisible) contactDetailsHistory = null;
            openingDetails = false;
        }
    }

    bool IsCurrentContact(ContactViewModel contact, string? accountId, Wallet? wallet) => isVisible && !switchingConversation
        && ReferenceEquals(Shell.Current?.CurrentPage, this) && CanOpenContactDetails && messagingService.IsEnabled
        && contact.Address == Address && ChatContact?.Address == contact.Address
        && accountId is not null && accountId == localAccountId && accountId == messagingService.AccountId
        && wallet is not null && ReferenceEquals(wallet, walletProvider.GetWallet())
        && wallet.GetDefaultAccount()?.Address == messagingService.Address;
}
