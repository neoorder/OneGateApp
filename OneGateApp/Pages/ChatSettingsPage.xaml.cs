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
    public bool CanRetry => isVisible && !IsWorking && !accountChanged && (!initialized || (retryUpdate is not null || HasAvatarEdits) && HasSettings);
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
        if (choosingAvatar) { NotifyState(); return; }
        appearanceGeneration++;
        initialized = false;
        accountChanged = false;
        retryUpdate = null;
        nicknameDraft = null;
        ClearAvatarDraft();
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
        if (choosingAvatar) { base.OnDisappearing(); return; }
        walletProvider.WalletChanged -= OnWalletChanged;
        messagingService.Changed -= OnMessagingChanged;
        operation?.Cancel();
        operation = null;
        BusyText = null;
        initialized = false;
        retryUpdate = null;
        nicknameDraft = null;
        ClearAvatarDraft();
        CloseBioEditor();
        NotifyState();
        base.OnDisappearing();
    }

    void OnWalletChanged(object? sender, Wallet? wallet) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if ((isVisible || choosingAvatar) && (!ReferenceEquals(settingsWallet, wallet)
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
        if (avatarRetry is not null)
        {
            int generation = appearanceGeneration;
            var retry = avatarRetry;
            try
            {
                var pending = await messagingService.GetPendingChatAvatarAsync(localAccountId, CancellationToken.None);
                if (!isVisible || !IsCurrentAccount || IsWorking || generation != appearanceGeneration
                    || !ReferenceEquals(retry, avatarRetry)) return;
                var profile = messagingService.GetChatDisplayProfile(localAccountId, settingsAccount!.Address);
                if (pending is null && profile is not null && profile.UpdatedAt > avatarRetryRevision
                    && (retry.Avatar.IsDeleted ? profile.Avatar is null : retry.Avatar.Value?.ToJson() == profile.Avatar?.ToJson()))
                {
                    avatarRetry = null;
                    if (uploadedAvatar is not null && uploadedAvatar.ToJson() == profile.Avatar?.ToJson())
                    {
                        avatarDraft = null;
                        uploadedAvatar = null;
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Meshline] Avatar retry state unavailable: {ex.Message}"); }
        }
        if (ErrorText == Strings.ChatSettingsSaveFailed || ErrorText == Strings.ChatAvatarSaveFailed)
            ErrorText = retryUpdate is not null ? Strings.ChatSettingsSaveFailed : HasAvatarEdits ? Strings.ChatAvatarSaveFailed : null;
    }

    void InvalidateAccount()
    {
        accountChanged = true;
        operation?.Cancel();
        retryUpdate = null;
        nicknameDraft = null;
        ClearAvatarDraft();
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
                avatarRetry = await messagingService.GetPendingChatAvatarAsync(localAccountId!, cancellation.Token);
                if (avatarRetry is not null) retryAvatarFirst = true;
                ErrorText = retryUpdate is not null ? Strings.ChatSettingsSaveFailed : avatarRetry is not null ? Strings.ChatAvatarSaveFailed : null;
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
            ErrorText = retryUpdate is not null ? Strings.ChatSettingsSaveFailed : HasAvatarEdits ? Strings.ChatAvatarSaveFailed : null;
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
        if (retryAvatarFirst && HasAvatarEdits) await RetryAvatarAsync();
        else if (retryUpdate is { } update) await SaveAsync(update);
        else if (HasAvatarEdits) await RetryAvatarAsync();
        else await LoadAsync();
    }

    Task RetryAvatarAsync() => avatarDraft is not null ? UploadAvatarDraftAsync()
        : avatarRetry is { } avatar ? SaveAsync(avatar) : Task.CompletedTask;

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
        bool editsAvatar = update.Avatar.IsSpecified || update.Avatar.IsDeleted;
        long profileRevision = messagingService.GetChatDisplayProfile(localAccountId, settingsAccount!.Address)?.UpdatedAt ?? -1;
        ErrorText = null;
        if (editsAvatar) { avatarRetry = update; avatarRetryRevision = profileRevision; }
        if (update.PublicDiscovery.IsSpecified) SetSwitch(update.PublicDiscovery.Value);
        NotifyState();
        try
        {
            var saved = await messagingService.UpdateChatProfileAsync(localAccountId, update, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrentAccount) { InvalidateAccount(); return; }
            if (update.Nickname.IsSpecified || update.Nickname.IsDeleted) nicknameDraft = null;
            if ((update.Bio.IsSpecified || update.Bio.IsDeleted) && BioDraft.Trim() == (saved.Bio ?? "")) CloseBioEditor();
            if (editsAvatar) ClearAvatarDraft();
        }
        catch (Exception) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!IsCurrentAccount) InvalidateAccount();
            else
            {
                retryAvatarFirst = editsAvatar;
                if (editsAvatar) avatarRetry = update;
                ErrorText = editsAvatar ? Strings.ChatAvatarSaveFailed : Strings.ChatSettingsSaveFailed;
            }
        }
        finally
        {
            if (ErrorText is null)
                ErrorText = retryUpdate is not null ? Strings.ChatSettingsSaveFailed : HasAvatarEdits ? Strings.ChatAvatarSaveFailed : null;
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
        OnPropertyChanged(nameof(CanEditAvatar));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(Address));
        OnPropertyChanged(nameof(Nickname));
        OnPropertyChanged(nameof(Bio));
        OnPropertyChanged(nameof(BusyText));
        OnPropertyChanged(nameof(ErrorText));
    }
}
