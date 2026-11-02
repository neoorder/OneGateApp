using Meshline.Components;
using Meshline.Models.Client;
using Meshline.Models.Protocol;
using Meshline.Transport;
using Meshline;
using Neo.Wallets;
using NeoOrder.OneGate.Properties;
using System.Diagnostics;

namespace NeoOrder.OneGate.Services.Messaging;

[Flags]
public enum MessagingChangeKind { Records = 1, SendStatus = 2, ReadState = 4 }

public sealed class MessagingChangedEventArgs(MessagingChangeKind kind) : EventArgs
{
    public MessagingChangeKind Kind { get; } = kind;
}

public sealed partial class MessagingService
{
    readonly object cancellationLock = new();
    CancellationTokenSource sessionCancellation = new();
    CancellationTokenSource? runtimeCancellation;
    Task runtimeTask = Task.CompletedTask;
    volatile bool isForeground;
    // Desktop processes can keep receiving while their windows are inactive or minimized.
    bool MaintainSession =>
#if WINDOWS || MACCATALYST
        true;
#else
        isForeground;
#endif
    int notificationQueued;

    /// <summary>Local messaging records changed. Delivered on the UI thread.</summary>
    public event EventHandler<MessagingChangedEventArgs>? Changed;

    public async Task SetForegroundAsync(bool foreground)
    {
        isForeground = foreground;
        if (foreground) RefreshVisibleChatProfiles();
        if (!MaintainSession) CancelSession();
        await UpdateSessionAsync();
        if (foreground)
            MainThread.BeginInvokeOnMainThread(() => UnreadCountChanged?.Invoke(this, EventArgs.Empty));
    }

    public async Task WalletChangedAsync(Wallet? wallet)
    {
        if (sessionWallet is not null && !ReferenceEquals(sessionWallet, wallet)) CancelSession();
        await UpdateSessionAsync();
    }

    async Task UpdateSessionAsync()
    {
        await lifecycleLock.WaitAsync();
        try
        {
            if (disposed) return;
            if (!MaintainSession || walletProvider.GetWallet()?.GetDefaultAccount() is null)
                await CloseCoreAsync();
            else
                await InitializeCoreAsync(CancellationToken.None);
            NotifyChanged();
        }
        finally { lifecycleLock.Release(); }
    }

    void CancelSession()
    {
        // Cancel network operations before waiting for the service lock.
        lock (cancellationLock) sessionCancellation.Cancel();
    }

    CancellationTokenSource CreateOperation(CancellationToken cancellationToken) =>
        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sessionCancellation.Token);

    void StartRuntime()
    {
        if (!MaintainSession || !IsEnabled || !runtimeTask.IsCompleted || sessionCancellation.IsCancellationRequested) return;
        runtimeCancellation?.Dispose();
        runtimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellation.Token);
        MeshlineClient current = client!;
        SetRuntimeConnectionState(current, RelayConnectionState.Connecting);
        CancellationToken token = runtimeCancellation.Token;
        // Starting the SDK resolves relays over the network; never hold the service lock for it.
        runtimeTask = Task.Run(() => RunRuntimeAsync(current, token));
    }

    async Task RunRuntimeAsync(MeshlineClient current, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    SetRuntimeConnectionState(current, RelayConnectionState.Connecting);
                    await current.ProfileManager.StartAsync(cancellationToken);
                    await current.MessageManager.StartAsync(cancellationToken);
                    SetRuntimeConnectionState(current, RelayConnectionState.Connected);
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    SetRuntimeConnectionState(current, RelayConnectionState.Disconnected);
                    System.Diagnostics.Debug.WriteLine($"[Meshline] Session startup failed; retrying: {ex}");
                    await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            try { await current.MessageManager.StopAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Meshline] Session stop failed: {ex}"); }
            try { await current.ProfileManager.StopAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Meshline] Profile session stop failed: {ex}"); }
        }
    }

    void OnMessagingChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, client?.MessageManager)) NotifyChanged();
    }

    /// <summary>Receive progress changed, without changing local message records. Delivered on the UI thread.</summary>
    public event EventHandler<ResourceSyncStatus>? SynchronizationChanged;

    void OnSynchronizationChanged(object? sender, ResourceSyncStatus status)
    {
        var current = client;
        if (!ReferenceEquals(sender, current?.MessageManager) || status.Resource != current?.Route?.RelayId) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (isForeground && !disposed && ReferenceEquals(current, client))
                SynchronizationChanged?.Invoke(this, status);
        });
    }

    void NotifyChanged(MessagingChangeKind kind = MessagingChangeKind.Records)
    {
        // Merge simultaneous changes; a state notification must not hide a record update.
        if (Interlocked.Or(ref notificationQueued, (int)kind) != 0) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var changes = (MessagingChangeKind)Interlocked.Exchange(ref notificationQueued, 0);
            if (isForeground && !disposed) Changed?.Invoke(this, new(changes));
        });
    }

    enum ConnectionStatus { Inactive, Connecting, Available, Unavailable, Offline }

    ConnectionStatus connectionStatus;
    volatile RelayConnectionState runtimeConnectionState;
    int connectionNotificationQueued;

    /// <summary>Connection presentation changed. Delivered on the UI thread without reloading records.</summary>
    public event EventHandler? ConnectionChanged;

    public string? ConnectionNotice => connectionStatus switch
    {
        ConnectionStatus.Unavailable or ConnectionStatus.Offline => Strings.ChatConnectionUnavailable,
        _ => null
    };

    ConnectionStatus GetConnectionStatus()
    {
        var current = client;
        var pool = relayPool;
        if (!isForeground || disposed || current is null || pool is null || !IsEnabled
            || !ReferenceEquals(sessionWallet, walletProvider.GetWallet())
            || !ReferenceEquals(sessionAccount, sessionWallet?.GetDefaultAccount()))
            return ConnectionStatus.Inactive;
        if (Connectivity.Current.NetworkAccess is NetworkAccess.None or NetworkAccess.Local or NetworkAccess.ConstrainedInternet)
            return ConnectionStatus.Offline;
        if (runtimeConnectionState != RelayConnectionState.Connected)
            return runtimeConnectionState == RelayConnectionState.Connecting
                ? ConnectionStatus.Connecting : ConnectionStatus.Unavailable;

        // Observe only our home relay's device session. Other peers and activation sessions
        // must not affect this notice. Reading the pool snapshot does not create connections.
        var relay = pool.Clients.FirstOrDefault(value => value.RelayId == current.Route?.RelayId
            && value.SessionMode == SessionMode.Device);
        if (relay is null) return ConnectionStatus.Connecting;
        if (relay.ConnectionState == RelayConnectionState.Connecting
            || relay.AuthenticationState == RelayAuthenticationState.Authenticating)
            return ConnectionStatus.Connecting;
        // This is observed relay communication, not proof of peer presence or delivery.
        return relay.ConnectionState == RelayConnectionState.Connected
            && relay.AuthenticationState == RelayAuthenticationState.Device
            ? ConnectionStatus.Available : ConnectionStatus.Unavailable;
    }

    void OnRelayConnectionChanged(object? sender, RelayClient relay)
    {
        if (!ReferenceEquals(sender, relayPool)) return;
        NotifyConnectionChanged();
    }

    void OnConnectionSourceChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, relayPool)) NotifyConnectionChanged();
        else if (ReferenceEquals(sender, client?.AccountManager) || ReferenceEquals(sender, client?.DeviceManager))
        {
            NotifyConnectionChanged();
            // Authorization changes must update chat availability without waiting for sync progress.
            NotifyChanged();
        }
    }

    void OnNetworkConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        NotifyConnectionChanged();
    }

    void SetRuntimeConnectionState(MeshlineClient current, RelayConnectionState state)
    {
        if (!ReferenceEquals(current, client)) return;
        runtimeConnectionState = state;
        NotifyConnectionChanged();
    }

    void NotifyConnectionChanged()
    {
        if (Interlocked.Exchange(ref connectionNotificationQueued, 1) != 0) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Interlocked.Exchange(ref connectionNotificationQueued, 0);
            // Re-read the current session so queued events cannot publish an old account's state.
            var status = GetConnectionStatus();
            if (connectionStatus == status) return;
            connectionStatus = status;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    static void OnMessagingBackgroundError(object? sender, BackgroundErrorEventArgs e) =>
        Debug.WriteLine($"[Meshline] [{DateTimeOffset.UtcNow:O}] Background {e.Operation}, code={(e.Error as RelayException)?.Error.Code ?? "(none)"}, resource={e.Resource ?? "(none)"}: {e.Error}");
}
