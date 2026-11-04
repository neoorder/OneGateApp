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
    ProfileUpdateState? profileUpdateState;
    ProfileUpdate profileDraft = new();
    readonly SemaphoreSlim profileStateReads = new(1, 1);
    bool profileStateUnavailable;
    int profileEditGeneration;
    string? submittedBio;
    string? nicknameDraft;
    bool editingNickname;
    int appearanceGeneration;

    public bool IsWorking => operation is not null;
    public bool ShowLoading => BusyText is not null;
    bool IsCurrentAccount => ReferenceEquals(settingsWallet, walletProvider.GetWallet())
        && ReferenceEquals(settingsAccount, settingsWallet?.GetDefaultAccount())
        && localAccountId == messagingService.AccountId;
    public bool HasSettings => initialized && !accountChanged && IsCurrentAccount && messagingService.IsEnabled;
    public bool CanEdit => isVisible && HasSettings && !IsWorking && !profileStateUnavailable;
    bool HasProfileFailure => profileUpdateState?.Status is ProfileUpdateStatus.WaitingToRetry or ProfileUpdateStatus.Failed;
    bool HasProfileDraft => HasChange(profileDraft.Nickname) || HasChange(profileDraft.Bio)
        || HasChange(profileDraft.Avatar) || HasChange(profileDraft.PublicDiscovery);
    public bool CanRetry => isVisible && !IsWorking && !accountChanged
        && (!initialized || HasSettings && (profileStateUnavailable || HasProfileFailure || HasProfileDraft || HasAvatarEdits));
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
        get => field ?? (HasSettings && profileUpdateState?.Status is ProfileUpdateStatus.Pending or ProfileUpdateStatus.Publishing
            ? Strings.ChatSettingsSaving : null);
        private set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowLoading));
        }
    }
    public string? ErrorText
    {
        get => field ?? (profileStateUnavailable ? Strings.ChatSettingsLoadFailed : HasProfileFailure ? Strings.ChatSettingsSaveFailed : null);
        private set;
    }

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
        profileUpdateState = null;
        profileDraft = new();
        profileStateUnavailable = false;
        nicknameDraft = null;
        ClearAvatarDraft();
        CloseBioEditor();
        settingsWallet = walletProvider.GetWallet();
        settingsAccount = settingsWallet?.GetDefaultAccount();
        walletProvider.WalletChanged += OnWalletChanged;
        messagingService.Changed += OnMessagingChanged;
        messagingService.ChatProfileUpdateStateChanged += OnMessagingChanged;
        await LoadAsync();
    }

    protected override void OnDisappearing()
    {
        isVisible = false;
        if (choosingAvatar) { base.OnDisappearing(); return; }
        walletProvider.WalletChanged -= OnWalletChanged;
        messagingService.Changed -= OnMessagingChanged;
        messagingService.ChatProfileUpdateStateChanged -= OnMessagingChanged;
        appearanceGeneration++;
        operation?.Cancel();
        operation = null;
        BusyText = null;
        initialized = false;
        profileUpdateState = null;
        profileDraft = new();
        profileStateUnavailable = false;
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
        if (!isVisible || !initialized || accountChanged) return;
        if (!IsCurrentAccount) InvalidateAccount();
        else
        {
            if (!messagingService.IsEnabled) ErrorText = Strings.ChatEnableFirst;
            else await RefreshProfileStateAsync();
            NotifyState();
        }
    }

    async Task RefreshProfileStateAsync(CancellationToken cancellationToken = default)
    {
        int generation = appearanceGeneration;
        string? accountId = localAccountId;
        await profileStateReads.WaitAsync(cancellationToken);
        try
        {
            if (!isVisible || !IsCurrentAccount || accountId is null || generation != appearanceGeneration) return;
            int edit = profileEditGeneration;
            var state = await messagingService.GetChatProfileUpdateStateAsync(accountId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!isVisible || !IsCurrentAccount || accountId != localAccountId || generation != appearanceGeneration || edit != profileEditGeneration) return;
            ApplyProfileState(state);
            RefreshDiscoverySwitch();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception)
        {
            if (isVisible && IsCurrentAccount && generation == appearanceGeneration) profileStateUnavailable = true;
        }
        finally { profileStateReads.Release(); NotifyState(); }
    }

    void InvalidateAccount()
    {
        accountChanged = true;
        operation?.Cancel();
        profileUpdateState = null;
        profileDraft = new();
        profileStateUnavailable = false;
        profileEditGeneration++;
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
                await RefreshProfileStateAsync(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (!IsCurrentAccount) { InvalidateAccount(); return; }
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
            var pending = profileUpdateState?.PendingUpdate?.Nickname ?? default;
            string initial = nicknameDraft ?? (pending.IsDeleted ? "" : pending.IsSpecified ? pending.Value : Nickname) ?? "";
            string? value = await DisplayPromptAsync(Strings.ChatNickname, Strings.ChatNicknameHint,
                Strings.OK, Strings.Cancel, initialValue: initial);
            if (value is null || !CanEdit || generation != appearanceGeneration) return;
            value = value.Trim();
            if (nicknameDraft is null && value == initial) return;
            await SaveAsync(new ProfileUpdate
            {
                Nickname = value.Length == 0 ? FieldUpdate<string>.Delete : value
            });
        }
        catch (Exception)
        {
            if (isVisible && generation == appearanceGeneration && IsCurrentAccount)
            {
                ErrorText = Strings.ChatSettingsSaveFailed;
                NotifyState();
            }
        }
        finally { editingNickname = false; }
    }

    void OnEditBioClicked(object sender, EventArgs e)
    {
        if (!CanEdit || IsEditingBio) return;
        var pending = HasChange(profileDraft.Bio) ? profileDraft.Bio : profileUpdateState?.PendingUpdate?.Bio ?? default;
        BioDraft = pending.IsSpecified ? pending.Value : pending.IsDeleted ? "" : Bio ?? "";
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
            ErrorText = null;
        NotifyState();
    }

    void CloseBioEditor()
    {
        IsEditingBio = false;
        BioDraft = "";
        submittedBio = null;
    }

    async void OnRetryClicked(object sender, EventArgs e) => await RetryAsync();

    async Task RetryAsync()
    {
        if (!CanRetry) return;
        if (!initialized || profileStateUnavailable) await LoadAsync();
        else if (avatarDraft is not null) await UploadAvatarDraftAsync();
        else await SaveAsync(HasProfileDraft ? profileDraft : null);
    }

    async Task SaveAsync(ProfileUpdate? update)
    {
        if (!CanEdit || localAccountId is null) return;
        if (update?.Nickname.IsSpecified == true) nicknameDraft = update.Nickname.Value;
        else if (update?.Nickname.IsDeleted == true) nicknameDraft = "";
        try { if (update is not null) MessagingService.ValidateChatProfileUpdate(localAccountId, update); }
        catch (ArgumentException ex)
        {
            ErrorText = ex.ParamName == nameof(update.Nickname) ? Strings.ChatNicknameTooLong
                : ex.ParamName == nameof(update.Bio) ? Strings.ChatBioTooLong : Strings.ChatProfileInvalid;
            NotifyState();
            return;
        }
        profileEditGeneration++;
        if (update is not null)
        {
            profileDraft = new()
            {
                Nickname = HasChange(update.Nickname) ? update.Nickname : profileDraft.Nickname,
                Bio = HasChange(update.Bio) ? update.Bio : profileDraft.Bio,
                Avatar = HasChange(update.Avatar) ? update.Avatar : profileDraft.Avatar,
                PublicDiscovery = HasChange(update.PublicDiscovery) ? update.PublicDiscovery : profileDraft.PublicDiscovery
            };
            if (HasChange(update.Bio)) submittedBio = update.Bio.IsDeleted ? "" : update.Bio.Value;
        }
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        BusyText = Strings.ChatSettingsSaving;
        bool editsAvatar = update is not null && HasChange(update.Avatar);
        ErrorText = null;
        if (update?.PublicDiscovery.IsSpecified == true) SetSwitch(update.PublicDiscovery.Value);
        NotifyState();
        try
        {
            if (update is null) await messagingService.RetryChatProfileUpdateAsync(localAccountId, cancellation.Token);
            else await messagingService.UpdateChatProfileAsync(localAccountId, update, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrentAccount) { InvalidateAccount(); return; }
            if (update is not null)
            {
                profileDraft = new()
                {
                    Nickname = HasChange(update.Nickname) ? default : profileDraft.Nickname,
                    Bio = HasChange(update.Bio) ? default : profileDraft.Bio,
                    Avatar = HasChange(update.Avatar) ? default : profileDraft.Avatar,
                    PublicDiscovery = HasChange(update.PublicDiscovery) ? default : profileDraft.PublicDiscovery
                };
                if (HasChange(update.Nickname)) nicknameDraft = null;
            }
            if (editsAvatar) ClearAvatarDraft();
        }
        catch (Exception) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!IsCurrentAccount) InvalidateAccount();
            else
            {
                ErrorText = editsAvatar ? Strings.ChatAvatarSaveFailed : Strings.ChatSettingsSaveFailed;
            }
        }
        finally
        {
            await FinishOperationAsync(cancellation);
        }
    }

    static bool HasChange<T>(FieldUpdate<T> field) where T : notnull => field.IsSpecified || field.IsDeleted;

    static bool SameChange<T>(FieldUpdate<T> left, FieldUpdate<T> right) where T : notnull =>
        left.IsDeleted && right.IsDeleted || left.IsSpecified && right.IsSpecified
        && (left.Value is ContentReference a && right.Value is ContentReference b
            ? a.ToJson() == b.ToJson() : EqualityComparer<T>.Default.Equals(left.Value, right.Value));

    static FieldUpdate<T> Unsubmitted<T>(FieldUpdate<T> draft, FieldUpdate<T> pending, FieldUpdate<T> confirmed) where T : notnull
        => SameChange(draft, HasChange(pending) ? pending : confirmed) ? default : draft;

    void ApplyProfileState(ProfileUpdateState state)
    {
        profileUpdateState = state;
        profileStateUnavailable = false;
        var pending = state.PendingUpdate;
        var previous = profileDraft;
        // Keep only edits not yet owned by the SDK; published intent lives exclusively there.
        profileDraft = new()
        {
            Nickname = Unsubmitted(previous.Nickname, pending?.Nickname ?? default,
                messagingService.ChatNickname is { } nickname ? nickname : FieldUpdate<string>.Delete),
            Bio = Unsubmitted(previous.Bio, pending?.Bio ?? default,
                messagingService.ChatBio is { } bio ? bio : FieldUpdate<string>.Delete),
            Avatar = Unsubmitted(previous.Avatar, pending?.Avatar ?? default,
                messagingService.ChatAvatar is { } avatar ? avatar : FieldUpdate<ContentReference>.Delete),
            PublicDiscovery = Unsubmitted(previous.PublicDiscovery, pending?.PublicDiscovery ?? default, messagingService.AllowAddressDiscovery)
        };
        if (HasChange(previous.Nickname) && !HasChange(profileDraft.Nickname)
            && nicknameDraft == (previous.Nickname.IsDeleted ? "" : previous.Nickname.Value)) nicknameDraft = null;
        if (submittedBio is not null && !HasChange(profileDraft.Bio) && !HasChange(pending?.Bio ?? default)
            && BioDraft.Trim() == submittedBio && (messagingService.ChatBio ?? "") == submittedBio) CloseBioEditor();
        if (uploadedAvatar is { } uploaded && !HasChange(Unsubmitted<ContentReference>(uploaded, pending?.Avatar ?? default,
            messagingService.ChatAvatar is { } confirmed ? confirmed : FieldUpdate<ContentReference>.Delete))) ClearAvatarDraft();
        if (!HasProfileDraft && !HasAvatarEdits && (ErrorText == Strings.ChatSettingsSaveFailed || ErrorText == Strings.ChatAvatarSaveFailed))
            ErrorText = null;
    }

    void RefreshDiscoverySwitch() => SetSwitch(profileDraft.PublicDiscovery.IsSpecified ? profileDraft.PublicDiscovery.Value
        : profileUpdateState?.PendingUpdate?.PublicDiscovery is { IsSpecified: true } pending ? pending.Value : messagingService.AllowAddressDiscovery);

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
            await RefreshProfileStateAsync();
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
