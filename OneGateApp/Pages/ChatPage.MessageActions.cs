using CommunityToolkit.Maui.Alerts;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;

namespace NeoOrder.OneGate.Pages;

public partial class ChatPage
{
    void OnMessageBubbleLoaded(object? sender, EventArgs e)
    {
#if WINDOWS || MACCATALYST
        if (sender is Microsoft.Maui.Controls.Border bubble && FlyoutBase.GetContextFlyout(bubble) is null)
            FlyoutBase.SetContextFlyout(bubble, (MenuFlyout)((DataTemplate)bubble.Resources["MessageContextMenu"]).CreateContent());
#endif
    }

    bool showingMessageActions;
    bool openingMessageLink;
    bool sharingMessageText;

    public Command<ChatMessageViewModel> MessageActionsCommand { get; }

    static ChatMessageViewModel? GetMessageActionParameter(object sender) => sender switch
    {
        Button button => button.CommandParameter as ChatMessageViewModel,
        MenuFlyoutItem item => item.CommandParameter as ChatMessageViewModel,
        _ => null
    };

    bool CanCopyMessage(ChatMessageViewModel message) => IsCurrentMessageContext(walletProvider.GetWallet(), localAccountId, Address)
        && message is { CanCopyText: true } && Messages.Contains(message);

    async Task ShowMessageActionsAsync(ChatMessageViewModel message)
    {
        if (showingMessageActions || openingMessageLink || sharingMessageText || forwardingMessage || !message.CanShowActions || !Messages.Contains(message)
            || !IsCurrentMessageContext(walletProvider.GetWallet(), localAccountId, Address)) return;
        showingMessageActions = true;
        var wallet = walletProvider.GetWallet();
        string? accountId = localAccountId;
        string address = Address;
        string? text = message.CopyText;
        try
        {
            List<string> actions = [];
            if (CanCompose && message.CanReply) actions.Add(Strings.ChatReply);
            if (HasLoaded && message.CanForwardText) actions.Add(Strings.ChatForwardText);
            if (message.CanCopyText)
            {
                actions.Add(Strings.ChatCopyText);
                actions.Add(Strings.Share);
                actions.Add(Strings.ChatViewFullText);
            }
            string action = await DisplayActionSheetAsync(null, Strings.Cancel, null, [.. actions]);
            if (!IsCurrentMessageContext(wallet, accountId, address) || !actions.Contains(action)) return;
            if (action == Strings.ChatReply) BeginReply(message);
            else if (action == Strings.ChatForwardText) await ForwardMessageAsync(message);
            else if (action == Strings.ChatCopyText) await Commands.Copy.ExecuteAsync(text);
            else if (action == Strings.Share) await ShareMessageTextAsync(text, wallet, accountId, address);
            else if (action == Strings.ChatViewFullText && Messages.Contains(message)) OpenMessageReader(text);
        }
        catch (Exception ex) { if (IsCurrentMessageContext(wallet, accountId, address)) ErrorText = ex.Message; }
        finally { showingMessageActions = false; }
    }

    async void OnCopyMessageClicked(object sender, EventArgs e)
    {
        if (sender is not MenuFlyoutItem { CommandParameter: ChatMessageViewModel message } || !CanCopyMessage(message)) return;
        var wallet = walletProvider.GetWallet();
        string? accountId = localAccountId;
        string address = Address;
        try { await Commands.Copy.ExecuteAsync(message.CopyText); }
        catch (Exception ex) { if (IsCurrentMessageContext(wallet, accountId, address)) ErrorText = ex.Message; }
    }

    async void OnShareMessageClicked(object sender, EventArgs e)
    {
        if (showingMessageActions || GetMessageActionParameter(sender) is not { } message || !CanCopyMessage(message)) return;
        await ShareMessageTextAsync(message.CopyText, walletProvider.GetWallet(), localAccountId, Address);
    }

    async Task ShareMessageTextAsync(string? text, Wallet? wallet, string? accountId, string address)
    {
        bool IsCurrent() => HasLoaded && !switchingConversation && !IsChatContentCovered
            && IsCurrentMessageContext(wallet, accountId, address);
        if (sharingMessageText || openingMessageLink || forwardingMessage || string.IsNullOrEmpty(text) || !IsCurrent()) return;
        sharingMessageText = true;
        try
        {
            // Share the selected text snapshot, even if a local refresh replaces its bubble.
            await Share.RequestAsync(new ShareTextRequest { Text = text, Title = Strings.Share });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (IsCurrent()) ErrorText = ex.Message; }
        finally { sharingMessageText = false; }
    }

    async Task OpenMessageLinkAsync(Uri link, Wallet? wallet, string? accountId, string address)
    {
        bool IsCurrent() => HasLoaded && !switchingConversation && !IsChatContentCovered
            && IsCurrentMessageContext(wallet, accountId, address);
        async Task ReportFailureAsync()
        {
            if (!IsCurrent()) return;
            await Toast.Show(Strings.ChatOpenLinkFailed);
        }
        if (openingMessageLink || sharingMessageText || forwardingMessage || !IsCurrent()) return;
        openingMessageLink = true;
        try
        {
            if (!await Browser.OpenAsync(link, BrowserLaunchMode.SystemPreferred)) await ReportFailureAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            await ReportFailureAsync();
        }
        finally { openingMessageLink = false; }
    }

    bool IsCurrentMessageContext(Wallet? wallet, string? accountId, string address) => isVisible && IsChatEnabled
        && ReferenceEquals(Shell.Current?.CurrentPage, this) && messagingService.IsEnabled && Address == address
        && accountId is not null && accountId == localAccountId && accountId == messagingService.AccountId
        && wallet is not null && ReferenceEquals(wallet, walletProvider.GetWallet())
        && wallet.GetDefaultAccount()?.Address == messagingService.Address;
}
