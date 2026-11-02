using CommunityToolkit.Maui.Extensions;
using Meshline.Models.Client;
using NeoOrder.OneGate.Controls.Popups;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services.Messaging;

namespace NeoOrder.OneGate.Pages;

public partial class HomePage
{
    readonly MessagingService messagingService;
    CancellationTokenSource? chatActivationCancellation;
    CancellationTokenSource? chatLoadCancellation;
    bool chatReloadPending;

    internal void ShowChatSection() => SelectedHomeSection = 0;

    public string? ChatConnectionNotice => IsChatEnabled && ChatAddress == messagingService.Address
        ? messagingService.ConnectionNotice : null;

    void OnChatConnectionChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(ChatConnectionNotice));

    public bool IsChatBusy { get; private set; }
    public bool IsChatEnabled { get; private set { field = value; OnPropertyChanged(); } }
    public bool HasChatState { get; private set; }
    public bool HasPendingChatActivation { get; private set; }
    public string? ChatAddress { get; private set; }
    public string? ChatError { get; private set; }
    public string ChatBusyText { get; private set; } = Strings.Loading;
    public bool IsEnablingChat => chatActivationCancellation is not null;
    public bool CanEnableChat => !IsChatBusy && !IsChatEnabled;
    public bool CanEditChatDiscovery => HasChatState && !IsChatBusy && !IsChatEnabled && !HasPendingChatActivation;
    public string ChatActionText => HasChatState && !HasPendingChatActivation && ChatError is null ? Strings.ChatEnable : Strings.Retry;
    public string ChatDiscoveryHint => AllowChatDiscovery ? Strings.ChatDiscoveryAllowedHint : Strings.ChatInvitationOnlyHint;

    public bool AllowChatDiscovery
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ChatDiscoveryHint));
        }
    } = true;

    async void LoadChatState()
    {
        chatReloadPending = true;
        if (IsChatBusy || !isPageVisible || SelectedHomeSection != 0) return;
        chatReloadPending = false;
        using var cancellation = new CancellationTokenSource();
        chatLoadCancellation = cancellation;
        IsChatBusy = true;
        ChatBusyText = Strings.Loading;
        ChatError = null;
        NotifyChatState();
        try
        {
            await ReadChatStateAsync(cancellation.Token);
            if (IsChatEnabled) await ReadConversationsAsync(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsChatEnabled)
                ConversationsError = ex.Message;
            else
            {
                HasChatState = false;
                ChatAddress = null;
                ChatError = ex.Message;
            }
        }
        finally
        {
            chatLoadCancellation = null;
            IsChatBusy = false;
            NotifyChatState();
            if (chatReloadPending && isPageVisible && SelectedHomeSection == 0) LoadChatState();
            else if (isPageVisible && SelectedHomeSection == 0 && IsChatEnabled) await messagingService.PromptPushGatewayAsync(this);
        }
    }

    async Task ReadChatStateAsync(CancellationToken cancellationToken = default)
    {
        await messagingService.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        bool accountChanged = ChatAddress != messagingService.Address;
        ChatAddress = messagingService.Address;
        if (accountChanged || !messagingService.IsEnabled) ClearConversations();
        IsChatEnabled = messagingService.IsEnabled;
        HasPendingChatActivation = messagingService.HasPendingActivation;
        if (accountChanged || IsChatEnabled || HasPendingChatActivation)
            AllowChatDiscovery = messagingService.AllowAddressDiscovery;
        HasChatState = true;
        RefreshPendingRequestCount();
    }

    async void OnEnableChatClicked(object sender, EventArgs e)
    {
        if (!CanEnableChat) return;
        if (!HasChatState)
        {
            LoadChatState();
            return;
        }

        using var cancellation = new CancellationTokenSource();
        chatActivationCancellation = cancellation;
        IsChatBusy = true;
        ChatBusyText = Strings.ChatEnabling;
        ChatError = null;
        NotifyChatState();
        try
        {
            await messagingService.EnableAsync(this, AllowChatDiscovery, SelectChatDeviceAsync, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Reload any saved setup progress; cancellation does not undo published state.
        }
        catch (AccountEstablishmentException ex) when (ex.Reason == AccountEstablishmentReason.ContactContinuityUnverifiable)
        {
            ChatError = Strings.ChatDeviceReplacementUnavailable;
        }
        catch (Exception ex)
        {
            ChatError = ex.Message;
        }
        finally
        {
            try
            {
                await ReadChatStateAsync();
            }
            catch (Exception ex)
            {
                HasChatState = false;
                IsChatEnabled = false;
                ChatAddress = null;
                ChatError ??= ex.Message;
            }
            chatActivationCancellation = null;
            IsChatBusy = false;
            NotifyChatState();
            if (isPageVisible && SelectedHomeSection == 0 && (IsChatEnabled || chatReloadPending)) LoadChatState();
        }
    }

    void OnCancelChatActivationClicked(object sender, EventArgs e) => chatActivationCancellation?.Cancel();

    async Task<string?> SelectChatDeviceAsync(DeviceSelectionInfo selection, string? error, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var popup = new SelectChatDevicePopup(selection, cancellationToken, error)
        {
            WidthRequest = Math.Min(420, Math.Max(240, Width - 60)),
            HeightRequest = Math.Min(600, Math.Max(240, Height - 100))
        };
        var result = await this.ShowPopupAsync<string?>(popup);
        cancellationToken.ThrowIfCancellationRequested();
        return result.Result;
    }

    void OnChatContactsClicked(object sender, EventArgs e) => SelectedHomeSection = 1;

    void NotifyChatState()
    {
        OnPropertyChanged(nameof(ChatConnectionNotice));
        OnPropertyChanged(nameof(IsChatBusy));
        OnPropertyChanged(nameof(ShowConversationLoading));
        OnPropertyChanged(nameof(IsChatEnabled));
        OnPropertyChanged(nameof(HasChatState));
        OnPropertyChanged(nameof(HasPendingChatActivation));
        OnPropertyChanged(nameof(ChatAddress));
        OnPropertyChanged(nameof(ChatError));
        OnPropertyChanged(nameof(ChatBusyText));
        OnPropertyChanged(nameof(IsEnablingChat));
        OnPropertyChanged(nameof(CanEnableChat));
        OnPropertyChanged(nameof(CanEditChatDiscovery));
        OnPropertyChanged(nameof(ChatActionText));
    }
}
