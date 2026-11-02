using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;

namespace NeoOrder.OneGate.Pages;

public partial class HomePage
{
    bool showingConversationActions;
    bool changingConversationPin;
    CancellationTokenSource? markConversationReadCancellation;

    public Command<ChatConversationViewModel> OpenConversationCommand { get; }
    public Command<ChatConversationViewModel> ConversationActionsCommand { get; }

    bool CanUseConversation(ChatConversationViewModel conversation) => isPageVisible && SelectedHomeSection == 0
        && ReferenceEquals(Shell.Current?.CurrentPage, this) && messagingService.IsEnabled
        && IsChatEnabled && conversation is not null && conversation.LocalAccountId == messagingService.AccountId
        && ReferenceEquals(conversationsWallet, walletProvider.GetWallet())
        && conversationsWallet?.GetDefaultAccount() is { } account && account.Address == messagingService.Address
        && allConversations.Any(value => value.Address == conversation.Address && value.LocalAccountId == conversation.LocalAccountId);

    async Task ShowConversationActionsAsync(ChatConversationViewModel conversation)
    {
        if (showingConversationActions || openingConversation || changingConversationPin || markConversationReadCancellation is not null
            || !CanUseConversation(conversation)) return;
        showingConversationActions = true;
        var wallet = conversationsWallet;
        try
        {
            List<string> actions = [conversation.PinActionText];
            if (conversation.HasUnread) actions.Add(Strings.ChatMarkAsRead);
            string action = await DisplayActionSheetAsync(null, Strings.Cancel, null, [.. actions]);
            if (!ReferenceEquals(wallet, conversationsWallet)) return;
            if (action == conversation.PinActionText) await SetConversationPinnedAsync(conversation, !conversation.IsPinned);
            else if (action == Strings.ChatMarkAsRead) await MarkConversationReadAsync(conversation);
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(wallet, conversationsWallet) && CanUseConversation(conversation)) ConversationsError = ex.Message;
        }
        finally { showingConversationActions = false; }
    }

    async void OnPinConversationClicked(object sender, EventArgs e)
    {
        if (sender is MenuFlyoutItem { CommandParameter: ChatConversationViewModel conversation })
            await SetConversationPinnedAsync(conversation, !conversation.IsPinned);
    }

    async Task SetConversationPinnedAsync(ChatConversationViewModel conversation, bool pinned)
    {
        if (changingConversationPin || !CanUseConversation(conversation)) return;
        changingConversationPin = true;
        var wallet = walletProvider.GetWallet();
        try
        {
            await messagingService.SetChatPinnedAsync(conversation.LocalAccountId, conversation.Address, pinned);
            if (!ReferenceEquals(wallet, walletProvider.GetWallet()) || !CanUseConversation(conversation)) return;
            allConversations = allConversations.Select(p => p.LocalAccountId == conversation.LocalAccountId
                && p.Address == conversation.Address ? p with { IsPinned = pinned } : p).ToArray();
            FilterConversations();
            ConversationsError = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(wallet, walletProvider.GetWallet()) && CanUseConversation(conversation))
            {
                ConversationsError = ex.Message;
            }
        }
        finally { changingConversationPin = false; }
    }

    async void OnMarkConversationReadClicked(object sender, EventArgs e)
    {
        if (sender is MenuFlyoutItem { CommandParameter: ChatConversationViewModel conversation })
            await MarkConversationReadAsync(conversation);
    }

    async Task MarkConversationReadAsync(ChatConversationViewModel conversation)
    {
        if (markConversationReadCancellation is not null || !CanUseConversation(conversation) || !conversation.HasUnread) return;
        using var cancellation = new CancellationTokenSource();
        markConversationReadCancellation = cancellation;
        var wallet = conversationsWallet;
        try
        {
            await messagingService.MarkChatReadAsync(conversation.LocalAccountId, conversation.Address,
                cancellationToken: cancellation.Token);
            // The service's change event refreshes both the list and the home badge from local state.
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested && ReferenceEquals(wallet, conversationsWallet)
                && CanUseConversation(conversation)) ConversationsError = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(markConversationReadCancellation, cancellation)) markConversationReadCancellation = null;
        }
    }
}
