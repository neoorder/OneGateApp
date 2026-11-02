using Meshline;
using NeoOrder.OneGate.Services.Notifications;
using Meshline.Interactions;
using Meshline.Models.Client;
using Meshline.Models.Protocol;
using Meshline.Storage;
using Meshline.Transport;
using Neo.Wallets;
using NeoOrder.OneGate.Properties;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace NeoOrder.OneGate.Services.Messaging;

public sealed partial class MessagingService(IWalletProvider walletProvider, IRelayRegistry registry,
    WalletAuthorizationService walletAuthorizationService, IServiceScopeFactory scopeFactory, PushGatewayOptions pushOptions, NeoOrder.OneGate.Services.Files.FileUploadClient fileUploads) : IAsyncDisposable
{
    readonly SemaphoreSlim lifecycleLock = new(1, 1);
    MeshlineClient? client;
    RelayClientPool? relayPool;
    Wallet? sessionWallet;
    WalletAccount? sessionAccount;
    string? activationPath;
    bool? pendingDiscovery;
    bool disposed;

    public string? AccountId { get; private set; }
    public string? Address => sessionAccount?.Address;
    public bool HasPendingActivation => pendingDiscovery.HasValue;
    public bool AllowAddressDiscovery => pendingDiscovery ?? client?.Profile?.PublicDiscovery ?? true;
    public bool IsEnabled => HasAuthorizedDevice && client?.Profile is not null && !HasPendingActivation;

    sealed record DeviceAuthorization(DeviceCertificate Local, AccountDeviceState State, DeviceCertificate? Registered);
    DeviceAuthorization? deviceAuthorization;

    bool HasAuthorizedDevice
    {
        get
        {
            if (client is not { Route: { } route, Device: { } device, DeviceState: { } state }) return false;
            var authorization = Volatile.Read(ref deviceAuthorization);
            if (authorization is null || !ReferenceEquals(authorization.Local, device) || !ReferenceEquals(authorization.State, state))
            {
                // These SDK models are immutable. Avoid serializing and hashing them on every UI state query.
                string deviceId = device.GetDeviceId(registry.Context);
                var registered = state.Certificates.FirstOrDefault(certificate => certificate.GetDeviceId(registry.Context) == deviceId);
                authorization = new(device, state, registered);
                Volatile.Write(ref deviceAuthorization, authorization);
            }
            // Registration can be reused, but validity must be checked at the time of use.
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return now < route.ExpiresAt && authorization.Registered is { } certificate
                && certificate.NotBefore <= now && now < certificate.ExpiresAt;
        }
    }

    /// <summary>Loads local state for the current account without enabling chat or requesting wallet signatures.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await InitializeCoreAsync(cancellationToken);
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    public async Task EnableAsync(Page page, bool allowAddressDiscovery,
        Func<DeviceSelectionInfo, string?, CancellationToken, Task<string?>> selectDevice,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selectDevice);
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await InitializeCoreAsync(cancellationToken);
            if (IsEnabled) return;

            Wallet wallet = sessionWallet!;
            WalletAccount account = sessionAccount!;
            bool discovery = pendingDiscovery ?? allowAddressDiscovery;
            string path = activationPath!;
            using var operation = CreateOperation(cancellationToken);
            void OnWalletChanged(object? sender, Wallet? current)
            {
                if (!ReferenceEquals(current, wallet)) operation.Cancel();
            }
            walletProvider.WalletChanged += OnWalletChanged;
            try
            {
                bool authorized = await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    operation.Token.ThrowIfCancellationRequested();
                    EnsureCurrentAccount(wallet, account);
                    return walletAuthorizationService.RequestAuthorizationAsync(page, Strings.ChatEnable,
                        string.Format(Strings.ChatAuthorizationMessage, account.Address));
                });
                operation.Token.ThrowIfCancellationRequested();
                EnsureCurrentAccount(wallet, account);
                if (!authorized) throw new OperationCanceledException(Strings.OperationCancelled);

                // Keep the exact choice across interrupted publication and application restarts.
                if (pendingDiscovery is null)
                {
                    await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(discovery), operation.Token);
                    operation.Token.ThrowIfCancellationRequested();
                    File.Move(path + ".tmp", path, overwrite: true);
                }

                try
                {
                    await InitializeCoreAsync(operation.Token, forActivation: true);
                    EnsureCurrentAccount(wallet, account);
                    // Initial preferences apply only to a new profile. Adding or renewing a device
                    // must preserve the account's existing profile and discovery setting.
                    var options = new AccountEstablishmentOptions
                    {
                        InitialProfile = new AccountInitialProfile { PublicDiscovery = discovery },
                        PushGateway = SupportsPushGateway ? pushOptions.CallbackUrl : null
                    };
                    while (true)
                    {
                        try
                        {
                            await client!.EstablishAccountAsync(options, operation.Token);
                            break;
                        }
                        catch (AccountEstablishmentException ex) when (
                            ex.DeviceSelection is not null && ex.Reason is AccountEstablishmentReason.DeviceSelectionRequired
                                or AccountEstablishmentReason.ContactContinuityUnverifiable)
                        {
                            string? selected = await MainThread.InvokeOnMainThreadAsync(() =>
                            {
                                operation.Token.ThrowIfCancellationRequested();
                                EnsureCurrentAccount(wallet, account);
                                string? error = ex.Reason == AccountEstablishmentReason.ContactContinuityUnverifiable
                                    ? Strings.ChatDeviceReplacementUnavailable : null;
                                return selectDevice(ex.DeviceSelection, error, operation.Token);
                            });
                            operation.Token.ThrowIfCancellationRequested();
                            EnsureCurrentAccount(wallet, account);
                            if (selected is null) throw new OperationCanceledException(Strings.OperationCancelled);
                            // The SDK rereads the latest state and publishes only this explicit replacement.
                            options.ReplaceDeviceId = selected;
                        }
                    }
                    operation.Token.ThrowIfCancellationRequested();
                    EnsureCurrentAccount(wallet, account);
                    File.Delete(path);
                }
                finally
                {
                    // The account signer is available only during this authorized operation.
                    await CloseCoreAsync();
                }
                await InitializeCoreAsync(operation.Token);
            }
            finally
            {
                walletProvider.WalletChanged -= OnWalletChanged;
            }
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    // Meshline SDK uses reflection to deserialize ContactGrant.Signatures.
    // Preserve the immutable dictionary factory required by System.Text.Json in trimmed builds.
    [DynamicDependency("CreateRange``2(System.Collections.Generic.IEnumerable{System.Collections.Generic.KeyValuePair{``0,``1}})",
        typeof(ImmutableDictionary))]
    async Task InitializeCoreAsync(CancellationToken cancellationToken, bool forActivation = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        Wallet? wallet = walletProvider.GetWallet();
        WalletAccount? account = wallet?.GetDefaultAccount();
        if (!ReferenceEquals(sessionWallet, wallet) || !ReferenceEquals(sessionAccount, account)
            || sessionCancellation.IsCancellationRequested)
            await CloseCoreAsync();

        if (wallet is null || account is null)
            throw new InvalidOperationException("A wallet with a default account is required for chat.");
        if (wallet.ProtocolSettings.Network != registry.Context.Reference)
            throw new InvalidOperationException("The wallet and Meshline registry use different Neo networks.");

        var identity = new NeoAccountSigner(account, wallet.ProtocolSettings.Network);
        await LoadChatDraftsAsync(identity.AccountId, cancellationToken);
        await LoadPendingMessagesAsync(identity.AccountId, cancellationToken);
        if (!forActivation && client is not null && AccountId == identity.AccountId)
        {
            StartRuntime();
            return;
        }
        await CloseCoreAsync();
        lock (cancellationLock)
        {
            if (sessionCancellation.IsCancellationRequested)
            {
                // A late page callback must not reopen a session after the app was suspended.
                if (!MaintainSession) sessionCancellation.Token.ThrowIfCancellationRequested();
                sessionCancellation.Dispose();
                sessionCancellation = new();
            }
        }
        using var initialization = CreateOperation(cancellationToken);
        cancellationToken = initialization.Token;

        var options = new ClientOptions { Context = registry.Context, AccountId = identity.AccountId };
        options.Validate();
        // Each installation keeps one local device per network, registry, and account.
        var database = new DatabaseOptions
        {
            Path = Path.Combine(FileSystem.AppDataDirectory, "meshline",
                registry.Context.Reference.ToString(CultureInfo.InvariantCulture),
                registry.Context.Registry, account.Address, "messages.db3")
        };
        Directory.CreateDirectory(Path.GetDirectoryName(database.Path)!);
        await MeshlineDatabase.MigrateAsync(database, cancellationToken);
        string path = Path.Combine(Path.GetDirectoryName(database.Path)!, "activation.json");
        bool? pending = File.Exists(path)
            ? JsonSerializer.Deserialize<bool>(await File.ReadAllTextAsync(path, cancellationToken))
            : null;

        // Let the pool own its HTTP client with the SDK's transport settings.
        var newPool = new RelayClientPool(options, registry);
        MeshlineClient? newClient = null;
        try
        {
            newClient = new MeshlineClient(options, database, newPool, new MeshlineSecretProtector(),
                forActivation ? identity : null);
#if DEBUG
            newClient.AccountManager.BackgroundError += OnMessagingBackgroundError;
            newClient.DeviceManager.BackgroundError += OnMessagingBackgroundError;
            newClient.MessageManager.BackgroundError += OnMessagingBackgroundError;
            newClient.ProfileManager.BackgroundError += OnMessagingBackgroundError;
#endif
            await newClient.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureCurrentAccount(wallet, account);

            sessionWallet = wallet;
            sessionAccount = account;
            AccountId = identity.AccountId;
            activationPath = path;
            pendingDiscovery = pending;
            relayPool = newPool;
            client = newClient;
            newClient.ProfileManager.ProfileChanged += OnChatProfileChanged;
            newPool.PoolChanged += OnConnectionSourceChanged;
            newPool.RelayChanged += OnRelayConnectionChanged;
            newClient.AccountManager.AccountChanged += OnConnectionSourceChanged;
            newClient.DeviceManager.DeviceStateChanged += OnConnectionSourceChanged;
            Connectivity.Current.ConnectivityChanged += OnNetworkConnectivityChanged;
            NotifyConnectionChanged();
            newClient.ConversationChanged += OnConversationUnreadChanged;
            newClient.MessageManager.ContactChanged += OnContactStateChanged;
            newClient.MessageManager.ContactRequestChanged += OnContactStateChanged;
            newClient.MessageManager.TimelineChanged += OnMessagingChanged;
            newClient.MessageManager.SendStatusChanged += OnMessageSendStatusChanged;
            newClient.MessageManager.SyncStatusChanged += OnSynchronizationChanged;
#if WINDOWS || MACCATALYST
            newClient.MessageManager.MessageReceived += OnIncomingDirectMessages;
            newClient.MessageManager.SyncStatusChanged += OnNotificationSyncStatusChanged;
            newClient.GroupManager.TimelineChanged += OnIncomingGroupMessages;
            newClient.GroupManager.SyncStatusChanged += OnNotificationSyncStatusChanged;
#endif
            if (!forActivation) StartRuntime();
        }
        catch
        {
            await DisposeClientAsync(newClient, newPool);
            throw;
        }
    }

    void EnsureCurrentAccount(Wallet wallet, WalletAccount account)
    {
        if (!ReferenceEquals(walletProvider.GetWallet(), wallet)
            || !ReferenceEquals(wallet.GetAccount(account.ScriptHash), account)
            || wallet.GetDefaultAccount()?.ScriptHash != account.ScriptHash)
            throw new InvalidOperationException("The wallet account changed during chat setup.");
    }

    public async Task CloseAsync()
    {
        CancelSession();
        await lifecycleLock.WaitAsync();
        try
        {
            await CloseCoreAsync();
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        CancelSession();
        await lifecycleLock.WaitAsync();
        try
        {
            if (disposed) return;
            disposed = true;
            await CloseCoreAsync();
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    async Task CloseCoreAsync()
    {
        await FlushChatDraftsAsync();
        await FlushPendingMessageStorageAsync();
        MeshlineClient? oldClient = client;
        if (oldClient is not null) oldClient.ProfileManager.ProfileChanged -= OnChatProfileChanged;
        RelayClientPool? oldPool = relayPool;
        client = null;
        Volatile.Write(ref deviceAuthorization, null);
        ClearChatNames();
        Interlocked.Increment(ref chatContactsRevision);
        Connectivity.Current.ConnectivityChanged -= OnNetworkConnectivityChanged;
        if (oldPool is not null)
        {
            oldPool.PoolChanged -= OnConnectionSourceChanged;
            oldPool.RelayChanged -= OnRelayConnectionChanged;
        }
        if (oldClient is not null)
        {
            oldClient.AccountManager.AccountChanged -= OnConnectionSourceChanged;
            oldClient.DeviceManager.DeviceStateChanged -= OnConnectionSourceChanged;
        }
        NotifyConnectionChanged();
        Interlocked.Increment(ref unreadRevision);
        ClearConversationFailureCache();
        Interlocked.Increment(ref requestCountRevision);
        runtimeCancellation?.Cancel();
        await runtimeTask;
#if WINDOWS || MACCATALYST
        notificationQueue?.Writer.TryComplete();
        await notificationTask;
        notificationQueue = null;
        ResetNotificationSync();
#endif
        runtimeCancellation?.Dispose();
        runtimeCancellation = null;
        if (oldClient is not null)
        {
            oldClient.ConversationChanged -= OnConversationUnreadChanged;
            oldClient.MessageManager.ContactChanged -= OnContactStateChanged;
            oldClient.MessageManager.ContactRequestChanged -= OnContactStateChanged;
            oldClient.MessageManager.TimelineChanged -= OnMessagingChanged;
            oldClient.MessageManager.SendStatusChanged -= OnMessageSendStatusChanged;
            oldClient.MessageManager.SyncStatusChanged -= OnSynchronizationChanged;
#if WINDOWS || MACCATALYST
            oldClient.MessageManager.MessageReceived -= OnIncomingDirectMessages;
            oldClient.MessageManager.SyncStatusChanged -= OnNotificationSyncStatusChanged;
            oldClient.GroupManager.TimelineChanged -= OnIncomingGroupMessages;
            oldClient.GroupManager.SyncStatusChanged -= OnNotificationSyncStatusChanged;
#endif
        }
        relayPool = null;
        sessionWallet = null;
        sessionAccount = null;
        activationPath = null;
        pendingDiscovery = null;
        AccountId = null;
        await DisposeClientAsync(oldClient, oldPool);
    }

    static async ValueTask DisposeClientAsync(MeshlineClient? client, RelayClientPool? pool)
    {
        try
        {
            if (client is not null) await client.DisposeAsync();
        }
        finally
        {
            if (pool is not null) await pool.DisposeAsync();
        }
    }
}
