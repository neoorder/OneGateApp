using Meshline.Models;
using Meshline.Models.Client;
using NeoOrder.OneGate.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    async void OnChatProfileChanged(object? sender, EventArgs e)
    {
        var current = client;
        if (current is null || !ReferenceEquals(sender, current.ProfileManager) || current.Profile is not { } profile) return;
        NotifyChatProfilesChanged();
        try
        {
            // ProfileChanged exposes verified/confirmed values, never the SDK's pending patch.
            await ReconcilePendingChatProfileAsync(profile);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Meshline] Profile completion storage failed: {ex.Message}"); }
        if (ReferenceEquals(current, client)) NotifyChanged();
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

    readonly SemaphoreSlim profileStorageLock = new(1, 1);
    string PendingProfileKey(string accountId) => $"chat/profile-pending/{registry.Context.Registry}/{accountId}";

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

    public async Task<ProfileUpdate?> GetPendingChatProfileAsync(string localAccountId, CancellationToken token)
    {
        ValidatePendingAccount(localAccountId);
        var pending = await ReconcilePendingChatProfileAsync(client!.Profile!, token: token);
        token.ThrowIfCancellationRequested();
        ValidatePendingAccount(localAccountId);
        return pending;
    }

    async Task<Guid?> StorePendingChatProfileAsync(string accountId, ProfileUpdate update, CancellationToken token)
    {
        bool nickname = update.Nickname.IsSpecified || update.Nickname.IsDeleted;
        bool bio = update.Bio.IsSpecified || update.Bio.IsDeleted;
        if (!nickname && !bio && !update.PublicDiscovery.IsSpecified) return null;
        await profileStorageLock.WaitAsync(token);
        try
        {
            ValidatePendingAccount(accountId);
            using var scope = scopeFactory.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var previous = await ReadPendingChatProfileAsync(database, accountId) ?? new();
            token.ThrowIfCancellationRequested();
            ValidatePendingAccount(accountId);
            long revision = client!.Profile!.UpdatedAt;
            var pending = previous with
            {
                OperationId = Guid.NewGuid(),
                Nickname = nickname ? update.Nickname.IsDeleted ? null : update.Nickname.Value : previous.Nickname,
                NicknameRevision = nickname ? revision : previous.NicknameRevision,
                Bio = bio ? update.Bio.IsDeleted ? null : update.Bio.Value : previous.Bio,
                BioRevision = bio ? revision : previous.BioRevision,
                PublicDiscovery = update.PublicDiscovery.IsSpecified ? update.PublicDiscovery.Value : previous.PublicDiscovery,
                DiscoveryRevision = update.PublicDiscovery.IsSpecified ? revision : previous.DiscoveryRevision
            };
            await database.Settings.PutAsync(PendingProfileKey(accountId), JsonSerializer.Serialize(pending, ChatProfileJsonContext.Default.PendingChatProfile));
            return pending.OperationId;
        }
        finally { profileStorageLock.Release(); }
    }

    async Task<PendingChatProfile?> ReadPendingChatProfileAsync(ApplicationDbContext database, string accountId)
    {
        string? value = await database.Settings.GetAsync(PendingProfileKey(accountId));
        return value is null ? null : JsonSerializer.Deserialize(value, ChatProfileJsonContext.Default.PendingChatProfile)
            ?? throw new InvalidDataException("Invalid pending profile.");
    }

    async Task<ProfileUpdate?> ReconcilePendingChatProfileAsync(AccountProfile profile, Guid? operationId = null,
        ProfileUpdate? acknowledged = null, CancellationToken token = default)
    {
        await profileStorageLock.WaitAsync(token);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var previous = await ReadPendingChatProfileAsync(database, profile.Account);
            if (previous is null) return null;
            var pending = previous;
            bool sameOperation = operationId == pending.OperationId;
            // Revisions survive restart; explicit success may acknowledge an unchanged revision.
            // A late response cannot acknowledge a newer operation, even with identical values.
            if (pending.NicknameRevision is { } nicknameRevision && pending.Nickname == profile.Nickname
                && (profile.UpdatedAt > nicknameRevision || sameOperation && (acknowledged?.Nickname.IsSpecified == true || acknowledged?.Nickname.IsDeleted == true)))
                pending = pending with { Nickname = null, NicknameRevision = null };
            if (pending.BioRevision is { } bioRevision && pending.Bio == profile.Bio
                && (profile.UpdatedAt > bioRevision || sameOperation && (acknowledged?.Bio.IsSpecified == true || acknowledged?.Bio.IsDeleted == true)))
                pending = pending with { Bio = null, BioRevision = null };
            if (pending.DiscoveryRevision is { } discoveryRevision && pending.PublicDiscovery == profile.PublicDiscovery
                && (profile.UpdatedAt > discoveryRevision || sameOperation && acknowledged?.PublicDiscovery.IsSpecified == true))
                pending = pending with { PublicDiscovery = false, DiscoveryRevision = null };
            var update = pending.ToUpdate();
            if (update is null) await database.Settings.DeleteAsync(PendingProfileKey(profile.Account));
            else if (pending != previous)
                await database.Settings.PutAsync(PendingProfileKey(profile.Account), JsonSerializer.Serialize(pending, ChatProfileJsonContext.Default.PendingChatProfile));
            return update;
        }
        finally { profileStorageLock.Release(); }
    }

    public async Task<AccountProfile> UpdateChatProfileAsync(string localAccountId, ProfileUpdate update,
        CancellationToken cancellationToken = default)
    {
        ValidateChatProfileUpdate(localAccountId, update);
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
                if (!ReferenceEquals(changedWallet, wallet)) operation.Cancel();
            }
            walletProvider.WalletChanged += OnWalletChanged;
            try
            {
                operation.Token.ThrowIfCancellationRequested();
                EnsureCurrentAccount(wallet, account);
                if (!ReferenceEquals(current, client) || AccountId != localAccountId || !IsEnabled)
                    throw new OperationCanceledException(operation.Token);
                // Publishing may wait for the relay; local chat operations must remain available.
                // A retry must reach the SDK even if local state already matches after an interrupted response.
                var publication = await StorePendingChatProfileAsync(localAccountId, update, operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                var profile = await current.ProfileManager.UpdateProfileAsync(update, operation.Token);
                await ReconcilePendingChatProfileAsync(profile, publication, update);
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

internal sealed record PendingChatProfile
{
    public Guid OperationId { get; init; }
    public string? Nickname { get; init; }
    public long? NicknameRevision { get; init; }
    public string? Bio { get; init; }
    public long? BioRevision { get; init; }
    public bool PublicDiscovery { get; init; }
    public long? DiscoveryRevision { get; init; }

    public ProfileUpdate? ToUpdate() => NicknameRevision is null && BioRevision is null && DiscoveryRevision is null ? null : new()
    {
        Nickname = NicknameRevision is null ? FieldUpdate<string>.Unspecified : Nickname is null ? FieldUpdate<string>.Delete : Nickname,
        Bio = BioRevision is null ? FieldUpdate<string>.Unspecified : Bio is null ? FieldUpdate<string>.Delete : Bio,
        PublicDiscovery = DiscoveryRevision is null ? FieldUpdate<bool>.Unspecified : PublicDiscovery
    };
}

[JsonSerializable(typeof(PendingChatProfile))]
internal partial class ChatProfileJsonContext : JsonSerializerContext;
