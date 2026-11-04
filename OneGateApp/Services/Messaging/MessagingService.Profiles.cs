using Meshline.Models.Client;
using System.Text;
using Meshline.Models.Protocol;
using Meshline;
using Neo.Wallets;
using NeoOrder.OneGate.Properties;

namespace NeoOrder.OneGate.Services.Messaging;

public sealed partial class MessagingService
{
    /// <summary>Reads a peer's verified profile through the SDK's demand-driven cache.</summary>
    public async Task<AccountProfile?> GetChatProfileAsync(string localAccountId, string address,
        CancellationToken cancellationToken = default, bool refresh = false)
    {
        MeshlineClient current;
        Wallet wallet;
        WalletAccount account;
        string peer;
        CancellationTokenSource operation;
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!IsEnabled) throw new InvalidOperationException(Strings.ChatEnableFirst);
            if (AccountId != localAccountId) throw new InvalidOperationException(Strings.ChatAccountChanged);
            peer = address == Address ? localAccountId : GetContactAccountId(address);
            current = client!;
            wallet = sessionWallet!;
            account = sessionAccount!;
            EnsureCurrentAccount(wallet, account);
            operation = CreateOperation(cancellationToken);
        }
        finally { lifecycleLock.Release(); }

        using (operation)
        {
            // A cache miss may need the network; it must not hold up sending or local history reads.
            var profile = refresh ? await current.ProfileManager.RefreshProfileAsync(peer, operation.Token)
                : await current.ProfileManager.GetProfileAsync(peer, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            EnsureCurrentAccount(wallet, account);
            if (!ReferenceEquals(current, client) || AccountId != localAccountId || !IsEnabled)
                throw new OperationCanceledException(operation.Token);
            CacheChatProfile(current, localAccountId, address, profile);
            return profile;
        }
    }

    sealed class ChatProfileSnapshot
    {
        public AccountProfile? Profile;
        public Task? Loading;
        public bool Loaded;
        public bool RefreshRequested;
        public DateTimeOffset RefreshAfter;
    }

    long chatNamesRevision;
    public long ChatNamesRevision => Interlocked.Read(ref chatNamesRevision);
    public event EventHandler? ChatProfilesChanged;
    readonly object chatNamesLock = new();
    readonly SemaphoreSlim profileDownloads = new(4, 4);
    readonly Dictionary<(string AccountId, string Address), ChatProfileSnapshot> chatNames = [];

    public string? GetChatDisplayName(string localAccountId, string address, string? label = null)
    {
        _ = LoadChatDisplayProfileAsync(localAccountId, address, refresh: false);
        lock (chatNamesLock)
            return ResolveChatDisplayName(label, chatNames.GetValueOrDefault((localAccountId, address))?.Profile?.Nickname);
    }

    public AccountProfile? GetChatDisplayProfile(string localAccountId, string address)
    {
        if (AccountId != localAccountId || !IsEnabled) return null;
        if (address == Address) return client?.Profile;
        lock (chatNamesLock) return chatNames.GetValueOrDefault((localAccountId, address))?.Profile;
    }

    // Visible controls own demand. Coalesce repeated messages for the same sender and cap
    // concurrency without holding the chat lifecycle lock during network activity.
    public Task RefreshChatDisplayProfileAsync(string localAccountId, string address, bool force = false)
        => LoadChatDisplayProfileAsync(localAccountId, address, refresh: true, force);

    Task LoadChatDisplayProfileAsync(string localAccountId, string address, bool refresh, bool force = false)
    {
        var current = client;
        if (current is null || !isForeground || disposed || !IsEnabled || AccountId != localAccountId
            || !ReferenceEquals(sessionWallet, walletProvider.GetWallet())
            || !ReferenceEquals(sessionAccount, sessionWallet?.GetDefaultAccount())) return Task.CompletedTask;
        lock (chatNamesLock)
        {
            var key = (localAccountId, address);
            if (!chatNames.TryGetValue(key, out var snapshot)) chatNames[key] = snapshot = new();
            if (refresh && (force || DateTimeOffset.UtcNow >= snapshot.RefreshAfter)) snapshot.RefreshRequested = true;
            if (snapshot.Loading is { IsCompleted: false }) return snapshot.Loading;
            if (snapshot.Loaded && !snapshot.RefreshRequested) return Task.CompletedTask;

            return snapshot.Loading = Task.Run(async () =>
            {
                await profileDownloads.WaitAsync();
                bool shouldRefresh = false;
                try
                {
                    if (!ReferenceEquals(current, client)) return;
                    if (!snapshot.Loaded)
                    {
                        await GetChatProfileAsync(localAccountId, address);
                        snapshot.Loaded = true;
                    }

                    lock (chatNamesLock)
                    {
                        shouldRefresh = snapshot.RefreshRequested;
                        snapshot.RefreshRequested = false;
                        if (shouldRefresh) snapshot.RefreshAfter = DateTimeOffset.UtcNow.AddMinutes(5);
                    }
                    if (shouldRefresh) await GetChatProfileAsync(localAccountId, address, refresh: true);
                }
                catch (Exception)
                {
                    // An optional refresh failure preserves the verified cached profile.
                    lock (chatNamesLock)
                    {
                        snapshot.Loaded = true;
                        snapshot.RefreshRequested = false;
                        snapshot.RefreshAfter = DateTimeOffset.UtcNow.AddMinutes(1);
                    }
                }
                finally { profileDownloads.Release(); if (shouldRefresh) NotifyChatProfilesChanged(); }
            });
        }
    }

    void CacheChatProfile(MeshlineClient current, string localAccountId, string address, AccountProfile? profile)
    {
        bool nameChanged;
        bool changed;
        lock (chatNamesLock)
        {
            if (!ReferenceEquals(current, client) || AccountId != localAccountId) return;
            var key = (localAccountId, address);
            if (!chatNames.TryGetValue(key, out var snapshot)) chatNames[key] = snapshot = new();
            // An absent resolve result is not evidence that an existing avatar was removed.
            if (profile is null) return;
            nameChanged = snapshot.Profile?.Nickname != profile.Nickname;
            changed = snapshot.Profile?.ToJson() != profile.ToJson();
            snapshot.Profile = profile;
            if (nameChanged) Interlocked.Increment(ref chatNamesRevision);
        }
        if (nameChanged) NotifyChanged();
        if (changed) NotifyChatProfilesChanged();
    }

    void NotifyChatProfilesChanged() => MainThread.BeginInvokeOnMainThread(() => ChatProfilesChanged?.Invoke(this, EventArgs.Empty));
    void OnChatProfileChanged(object? sender, EventArgs e)
    {
        var current = client;
        if (current is null || !ReferenceEquals(sender, current.ProfileManager) || current.Profile is not { } profile) return;
        CacheChatProfile(current, profile.Account, Address!, profile);
        NotifyChanged();
    }

    /// <summary>The current account's SDK publication state changed. Delivered on the UI thread.</summary>
    public event EventHandler? ChatProfileUpdateStateChanged;

    void OnChatProfileUpdateStateChanged(object? sender, EventArgs e)
    {
        var current = client;
        if (current is null || !ReferenceEquals(sender, current.ProfileManager)) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (isForeground && !disposed && ReferenceEquals(current, client))
                ChatProfileUpdateStateChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void RefreshVisibleChatProfiles()
    {
        lock (chatNamesLock)
            foreach (var snapshot in chatNames.Values) snapshot.RefreshAfter = default;
        NotifyChatProfilesChanged();
    }

    void ClearChatNames()
    {
        lock (chatNamesLock)
        {
            chatNames.Clear();
            Interlocked.Increment(ref chatNamesRevision);
        }
        NotifyChatProfilesChanged();
    }

    internal static string? ResolveChatDisplayName(string? label, string? nickname)
    {
        if (!string.IsNullOrWhiteSpace(label)) return label;
        return !string.IsNullOrWhiteSpace(nickname) ? nickname : null;
    }

    public string? ChatNickname => client?.Profile?.Nickname;
    public string? ChatBio => client?.Profile?.Bio;
    public ContentReference? ChatAvatar => client?.Profile?.Avatar;

    internal static void ValidateChatProfileUpdate(string accountId, ProfileUpdate update)
    {
        if (update.Nickname.IsSpecified && Encoding.UTF8.GetByteCount(update.Nickname.Value ?? "") > 256)
            throw new ArgumentException("Nickname exceeds the UTF-8 size limit.", nameof(update.Nickname));
        if (update.Bio.IsSpecified && Encoding.UTF8.GetByteCount(update.Bio.Value ?? "") > 2048)
            throw new ArgumentException("Bio exceeds the UTF-8 size limit.", nameof(update.Bio));
        // Use the SDK model for the remaining field/document rules before retaining intent.
        var candidate = new AccountProfile
        {
            Account = accountId, Nickname = update.Nickname.IsSpecified ? update.Nickname.Value : null,
            Bio = update.Bio.IsSpecified ? update.Bio.Value : null,
            Avatar = update.Avatar.IsSpecified ? update.Avatar.Value : null,
            PublicDiscovery = update.PublicDiscovery.IsSpecified && update.PublicDiscovery.Value,
            UpdatedAt = 0, DeviceSignature = [.. new byte[64]]
        };
        if (update.PublicDiscovery.IsDeleted || candidate.Validate() is not null)
            throw new ArgumentException("Invalid profile update.", nameof(update));
    }

    public async Task<ProfileUpdateState> GetChatProfileUpdateStateAsync(string localAccountId, CancellationToken cancellationToken = default)
    {
        MeshlineClient current;
        Wallet wallet;
        WalletAccount account;
        CancellationTokenSource operation;
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ValidatePendingAccount(localAccountId);
            current = client!;
            wallet = sessionWallet!;
            account = sessionAccount!;
            operation = CreateOperation(cancellationToken);
        }
        finally { lifecycleLock.Release(); }

        using (operation)
        {
            var state = await current.ProfileManager.GetUpdateStateAsync(operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            EnsureCurrentAccount(wallet, account);
            if (!ReferenceEquals(current, client) || AccountId != localAccountId || !IsEnabled)
                throw new OperationCanceledException(operation.Token);
            return state;
        }
    }

    public async Task<AccountProfile> UpdateChatProfileAsync(string localAccountId, ProfileUpdate update,
        CancellationToken cancellationToken = default)
    {
        ValidateChatProfileUpdate(localAccountId, update);
        return (await PublishChatProfileAsync(localAccountId, update, cancellationToken))!;
    }

    // Retry the SDK's latest intent without resubmitting a page's older snapshot.
    public Task<AccountProfile?> RetryChatProfileUpdateAsync(string localAccountId, CancellationToken cancellationToken = default)
        => PublishChatProfileAsync(localAccountId, null, cancellationToken);

    async Task<AccountProfile?> PublishChatProfileAsync(string localAccountId, ProfileUpdate? update, CancellationToken cancellationToken)
    {
        MeshlineClient current;
        Wallet wallet;
        WalletAccount account;
        CancellationTokenSource operation;
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await InitializeCoreAsync(cancellationToken);
            if (!IsEnabled) throw new InvalidOperationException(Strings.ChatEnableFirst);
            if (AccountId != localAccountId) throw new InvalidOperationException(Strings.ChatAccountChanged);
            current = client!;
            wallet = sessionWallet!;
            account = sessionAccount!;
            EnsureCurrentAccount(wallet, account);
            operation = CreateOperation(cancellationToken);
        }
        finally { lifecycleLock.Release(); }

        using (operation)
        {
            void OnWalletChanged(object? sender, Wallet? changedWallet)
            {
                if (ReferenceEquals(changedWallet, wallet)) return;
                // Another wallet-change subscriber may already have canceled and completed this call.
                try { operation.Cancel(); }
                catch (ObjectDisposedException) { }
            }
            walletProvider.WalletChanged += OnWalletChanged;
            try
            {
                operation.Token.ThrowIfCancellationRequested();
                EnsureCurrentAccount(wallet, account);
                if (!ReferenceEquals(current, client) || AccountId != localAccountId || !IsEnabled)
                    throw new OperationCanceledException(operation.Token);
                // The SDK owns durable intent, merging, confirmation, and background retries.
                var profile = update is null
                    ? await current.ProfileManager.PublishProfileAsync(cancellationToken: operation.Token)
                    : await current.ProfileManager.UpdateProfileAsync(update, operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                EnsureCurrentAccount(wallet, account);
                if (!ReferenceEquals(current, client) || AccountId != localAccountId || !IsEnabled)
                    throw new OperationCanceledException(operation.Token);
                CacheChatProfile(current, localAccountId, account.Address, profile);
                return profile;
            }
            finally { walletProvider.WalletChanged -= OnWalletChanged; NotifyChanged(); }
        }
    }
}
