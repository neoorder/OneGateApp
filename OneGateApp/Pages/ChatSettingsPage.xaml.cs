using Meshline.Models;
using Meshline.Models.Client;
using Meshline.Models.Protocol;
using Neo.Wallets;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services.Messaging;

namespace NeoOrder.OneGate.Pages;

public partial class ChatSettingsPage : ContentPage
{
    readonly MessagingService messagingService;
    readonly IWalletProvider walletProvider;
    CancellationTokenSource? operation;
    Wallet? settingsWallet;
    WalletAccount? settingsAccount;
    string? localAccountId;
    bool isVisible;
    bool initialized;
    bool accountChanged;
    bool updatingSwitch;
    ProfileUpdate? retryUpdate;
    string? nicknameDraft;
    bool editingNickname;
    int appearanceGeneration;

    public bool IsWorking => operation is not null;
    public bool ShowLoading => IsWorking && BusyText is not null;
    bool IsCurrentAccount => ReferenceEquals(settingsWallet, walletProvider.GetWallet())
        && ReferenceEquals(settingsAccount, settingsWallet?.GetDefaultAccount())
        && localAccountId == messagingService.AccountId;
    public bool HasSettings => initialized && !accountChanged && IsCurrentAccount && messagingService.IsEnabled;
    public bool CanEdit => isVisible && HasSettings && !IsWorking;
    public bool CanRetry => isVisible && !IsWorking && !accountChanged && (!initialized || retryUpdate is not null && HasSettings);
    public string? Address => !accountChanged ? settingsAccount?.Address : null;
    public string? Nickname => HasSettings ? messagingService.ChatNickname : null;
    public string? Bio => HasSettings ? messagingService.ChatBio : null;
    public bool IsEditingBio { get; private set { field = value; OnPropertyChanged(); } }
    public string BioDraft
    {
        get;
        set
        {
            value ??= "";
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    } = "";
    public bool AllowAddressDiscovery { get; private set; }
    public string DiscoveryHint => AllowAddressDiscovery ? Strings.ChatDiscoveryAllowedHint : Strings.ChatInvitationOnlyHint;
    public string? BusyText
    {
        get;
        private set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowLoading));
        }
    }
    public string? ErrorText { get; private set; }

    public ChatSettingsPage(MessagingService messagingService, IWalletProvider walletProvider)
    {
        this.messagingService = messagingService;
        this.walletProvider = walletProvider;
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        isVisible = true;
        appearanceGeneration++;
        initialized = false;
        accountChanged = false;
        retryUpdate = null;
        nicknameDraft = null;
        CloseBioEditor();
        settingsWallet = walletProvider.GetWallet();
        settingsAccount = settingsWallet?.GetDefaultAccount();
        walletProvider.WalletChanged += OnWalletChanged;
        messagingService.Changed += OnMessagingChanged;
        await LoadAsync();
    }

    protected override void OnDisappearing()
    {
        isVisible = false;
        walletProvider.WalletChanged -= OnWalletChanged;
        messagingService.Changed -= OnMessagingChanged;
        operation?.Cancel();
        operation = null;
        BusyText = null;
        initialized = false;
        retryUpdate = null;
        nicknameDraft = null;
        CloseBioEditor();
        NotifyState();
        base.OnDisappearing();
    }

    void OnWalletChanged(object? sender, Wallet? wallet) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (isVisible && (!ReferenceEquals(settingsWallet, wallet)
            || !ReferenceEquals(settingsAccount, settingsWallet?.GetDefaultAccount()))) InvalidateAccount();
    });

    async void OnMessagingChanged(object? sender, EventArgs e)
    {
        if (!isVisible || !initialized || IsWorking || accountChanged) return;
        if (!IsCurrentAccount) InvalidateAccount();
        else
        {
            if (!messagingService.IsEnabled) ErrorText = Strings.ChatEnableFirst;
            else await RefreshConfirmedEditsAsync();
            NotifyState();
        }
    }

    async Task RefreshConfirmedEditsAsync()
    {
        if (!IsCurrentAccount || localAccountId is null || IsWorking) return;
        int profileGeneration = appearanceGeneration;
        try
        {
            var pending = await messagingService.GetPendingChatProfileAsync(localAccountId, CancellationToken.None);
            if (!isVisible || !IsCurrentAccount || IsWorking || profileGeneration != appearanceGeneration) return;
            ApplyPendingProfile(pending);
        }
        catch (Exception)
        {
            if (isVisible && IsCurrentAccount && profileGeneration == appearanceGeneration)
                ErrorText = Strings.ChatSettingsSaveFailed;
            return;
        }
        RefreshDiscoverySwitch();
        if (ErrorText == Strings.ChatSettingsSaveFailed)
            ErrorText = retryUpdate is not null ? Strings.ChatSettingsSaveFailed : null;
    }

    void InvalidateAccount()
    {
        accountChanged = true;
        operation?.Cancel();
        retryUpdate = null;
        nicknameDraft = null;
        CloseBioEditor();
        ErrorText = Strings.ChatAccountChanged;
        NotifyState();
    }

    async Task LoadAsync()
    {
        if (IsWorking || !isVisible || accountChanged) return;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        BusyText = Strings.Loading;
        ErrorText = null;
        NotifyState();
        try
        {
            await messagingService.InitializeAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            localAccountId = messagingService.AccountId;
            if (!IsCurrentAccount) { InvalidateAccount(); return; }
            SetSwitch(messagingService.AllowAddressDiscovery);
            if (!messagingService.IsEnabled) ErrorText = Strings.ChatEnableFirst;
            else
            {
                var pending = await messagingService.GetPendingChatProfileAsync(localAccountId!, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (!IsCurrentAccount) { InvalidateAccount(); return; }
                ApplyPendingProfile(pending);
                RefreshDiscoverySwitch();
                ErrorText = retryUpdate is not null ? Strings.ChatSettingsSaveFailed : null;
            }
            initialized = true;
        }
        catch (Exception) when (cancellation.IsCancellationRequested) { }
        catch (Exception) { initialized = false; ErrorText = Strings.ChatSettingsLoadFailed; }
        finally { await FinishOperationAsync(cancellation); }
    }

    async void OnDiscoveryToggled(object sender, ToggledEventArgs e)
    {
        if (!updatingSwitch && CanEdit && e.Value != AllowAddressDiscovery)
            await SaveAsync(new ProfileUpdate { PublicDiscovery = e.Value });
    }

    async void OnEditNicknameClicked(object sender, EventArgs e)
    {
        if (!CanEdit || editingNickname) return;
        editingNickname = true;
        int generation = appearanceGeneration;
        try
        {
            // Keep an unconfirmed edit available when the user opens the editor again.
            string initial = nicknameDraft ?? Nickname ?? "";
            string? value = await DisplayPromptAsync(Strings.ChatNickname, Strings.ChatNicknameHint,
                Strings.OK, Strings.Cancel, initialValue: initial);
            if (value is null || !CanEdit || generation != appearanceGeneration) return;
            value = value.Trim();
            if (nicknameDraft is null && value == (Nickname ?? "")) return;
            await SaveAsync(new ProfileUpdate
            {
                Nickname = value.Length == 0 ? FieldUpdate<string>.Delete : value
            });
        }
        catch (Exception ex)
        {
            if (isVisible && generation == appearanceGeneration && IsCurrentAccount)
            {
                ErrorText = ex.Message;
                NotifyState();
            }
        }
        finally { editingNickname = false; }
    }

    void OnEditBioClicked(object sender, EventArgs e)
    {
        if (!CanEdit || IsEditingBio) return;
        BioDraft = retryUpdate?.Bio is { IsSpecified: true } pending ? pending.Value ?? ""
            : retryUpdate?.Bio.IsDeleted == true ? "" : Bio ?? "";
        IsEditingBio = true;
    }

    async void OnSaveBioClicked(object sender, EventArgs e)
    {
        if (!CanEdit || !IsEditingBio) return;
        string value = BioDraft.Trim();
        await SaveAsync(new ProfileUpdate
        {
            Bio = value.Length == 0 ? FieldUpdate<string>.Delete : value
        });
    }

    void OnCancelBioClicked(object sender, EventArgs e)
    {
        if (!CanEdit || !IsEditingBio) return;
        CloseBioEditor();
        if (ErrorText == Strings.ChatBioTooLong || ErrorText == Strings.ChatProfileInvalid)
            ErrorText = retryUpdate is not null ? Strings.ChatSettingsSaveFailed : null;
        NotifyState();
    }

    void CloseBioEditor()
    {
        IsEditingBio = false;
        BioDraft = "";
    }

    async void OnRetryClicked(object sender, EventArgs e) => await RetryAsync();

    async Task RetryAsync()
    {
        if (!CanRetry) return;
        if (retryUpdate is { } update) await SaveAsync(update);
        else await LoadAsync();
    }

    async Task SaveAsync(ProfileUpdate update)
    {
        if (!CanEdit || localAccountId is null) return;
        if (update.Nickname.IsSpecified) nicknameDraft = update.Nickname.Value;
        else if (update.Nickname.IsDeleted) nicknameDraft = "";
        try { MessagingService.ValidateChatProfileUpdate(localAccountId, update); }
        catch (ArgumentException ex)
        {
            ErrorText = ex.ParamName == nameof(update.Nickname) ? Strings.ChatNicknameTooLong
                : ex.ParamName == nameof(update.Bio) ? Strings.ChatBioTooLong : Strings.ChatProfileInvalid;
            NotifyState();
            return;
        }
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        BusyText = Strings.ChatSettingsSaving;
        ErrorText = null;
        if (update.PublicDiscovery.IsSpecified) SetSwitch(update.PublicDiscovery.Value);
        NotifyState();
        try
        {
            var saved = await messagingService.UpdateChatProfileAsync(localAccountId, update, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrentAccount) { InvalidateAccount(); return; }
            if (update.Nickname.IsSpecified || update.Nickname.IsDeleted) nicknameDraft = null;
            if ((update.Bio.IsSpecified || update.Bio.IsDeleted) && BioDraft.Trim() == (saved.Bio ?? "")) CloseBioEditor();
        }
        catch (Exception) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!IsCurrentAccount) InvalidateAccount();
            else
            {
                ErrorText = Strings.ChatSettingsSaveFailed;
            }
        }
        finally
        {
            if (ErrorText is null)
                ErrorText = retryUpdate is not null ? Strings.ChatSettingsSaveFailed : null;
            if (!cancellation.IsCancellationRequested && IsCurrentAccount)
                RefreshDiscoverySwitch();
            await FinishOperationAsync(cancellation);
        }
    }

    void ApplyPendingProfile(ProfileUpdate? pending)
    {
        var previous = retryUpdate;
        retryUpdate = pending;
        if (pending is { } update && (update.Nickname.IsSpecified || update.Nickname.IsDeleted))
            nicknameDraft ??= update.Nickname.IsDeleted ? "" : update.Nickname.Value;
        else if (previous is { } old && (old.Nickname.IsSpecified || old.Nickname.IsDeleted)
            && nicknameDraft == (old.Nickname.IsDeleted ? "" : old.Nickname.Value)) nicknameDraft = null;
        if (previous is { } bio && (bio.Bio.IsSpecified || bio.Bio.IsDeleted)
            && pending?.Bio.IsSpecified != true && pending?.Bio.IsDeleted != true
            && BioDraft.Trim() == (Bio ?? "")) CloseBioEditor();
    }

    void RefreshDiscoverySwitch() => SetSwitch(retryUpdate?.PublicDiscovery is { IsSpecified: true } pending
        ? pending.Value : messagingService.AllowAddressDiscovery);

    void SetSwitch(bool value)
    {
        updatingSwitch = true;
        try
        {
            AllowAddressDiscovery = value;
            OnPropertyChanged(nameof(AllowAddressDiscovery));
            OnPropertyChanged(nameof(DiscoveryHint));
        }
        finally { updatingSwitch = false; }
    }

    async Task FinishOperationAsync(CancellationTokenSource cancellation)
    {
        if (!ReferenceEquals(operation, cancellation)) return;
        operation = null;
        BusyText = null;
        if (!cancellation.IsCancellationRequested && initialized && isVisible && IsCurrentAccount)
            await RefreshConfirmedEditsAsync();
        NotifyState();
    }

    void NotifyState()
    {
        OnPropertyChanged(nameof(IsWorking));
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(HasSettings));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(Address));
        OnPropertyChanged(nameof(Nickname));
        OnPropertyChanged(nameof(Bio));
        OnPropertyChanged(nameof(BusyText));
        OnPropertyChanged(nameof(ErrorText));
    }
}
