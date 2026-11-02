#if WINDOWS || MACCATALYST
using Meshline;
using Meshline.Components;
using Meshline.Models.Client;
using Meshline.Models.Protocol;
using Meshline.Transport;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services.Notifications;
using System.Globalization;
using System.Threading.Channels;

namespace NeoOrder.OneGate.Services.Messaging;

public sealed partial class MessagingService
{
    readonly NotificationSyncState notificationSync = new();
    Channel<IncomingNotification>? notificationQueue;
    Task notificationTask = Task.CompletedTask;

    sealed record IncomingNotification(string Resource, string Sender, string MessageId, string DeviceId, long Generation, GroupRef? Group);

    void OnNotificationSyncStatusChanged(object? sender, ResourceSyncStatus status)
    {
        var current = client;
        if (current is null || !(ReferenceEquals(sender, current.MessageManager) || ReferenceEquals(sender, current.GroupManager))) return;
        // Synchronizing is also emitted for live updates; only catch-up after a pause rearms alerts.
        if (status.State == ResourceSyncState.CaughtUp) notificationSync.CaughtUp(status.Resource);
        else if (status.State is ResourceSyncState.Idle or ResourceSyncState.Blocked) notificationSync.Pause(status.Resource);
    }

    void ResetNotificationSync() => notificationSync.Reset();

    bool CanNotify(MeshlineClient current, string resource, string deviceId, long generation)
    {
        if (isForeground || disposed || !NotificationPreferences.Enabled || !NotificationPreferences.IsChatEnabled(deviceId)
            || !ReferenceEquals(client, current) || !IsEnabled
            || !ReferenceEquals(sessionWallet, walletProvider.GetWallet())
            || !ReferenceEquals(sessionAccount, sessionWallet?.GetDefaultAccount())
            || current.Device?.GetDeviceId(registry.Context) != deviceId
            || Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return false;
        return notificationSync.IsCurrent(resource, generation);
    }

    void OnIncomingDirectMessages(object? sender, MessageReceivedEventArgs args)
    {
        var current = client;
        if (current?.Device is not { } device || current.Route is not { } route || !ReferenceEquals(sender, current.MessageManager)) return;
        string deviceId = device.GetDeviceId(registry.Context);
        if (notificationSync.Capture(route.RelayId) is not { } generation || !CanNotify(current, route.RelayId, deviceId, generation)) return;
        foreach (var message in args.Messages)
            if (message.Recipient == AccountId && message.Key.Sender != AccountId)
                notificationQueue?.Writer.TryWrite(new(route.RelayId, message.Key.Sender, message.Key.MessageId, deviceId, generation, null));
    }

    void OnIncomingGroupMessages(object? sender, GroupTimelineChangedEventArgs args)
    {
        var current = client;
        if (current?.Device is not { } device || !ReferenceEquals(sender, current.GroupManager)) return;
        string deviceId = device.GetDeviceId(registry.Context);
        foreach (var message in args.Messages)
            if (message.Sender != AccountId && notificationSync.Capture(message.Group.GroupId) is { } generation
                && CanNotify(current, message.Group.GroupId, deviceId, generation))
                notificationQueue?.Writer.TryWrite(new(message.Group.GroupId, message.Sender, message.MessageId, deviceId, generation, message.Group));
    }

    void StartDesktopNotifications(MeshlineClient current, CancellationToken token)
    {
        ResetNotificationSync();
        notificationQueue = Channel.CreateBounded<IncomingNotification>(new BoundedChannelOptions(256)
        {
            SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropOldest
        });
        notificationTask = ProcessDesktopNotificationsAsync(current, notificationQueue.Reader, token);
    }

    async Task ProcessDesktopNotificationsAsync(MeshlineClient current, ChannelReader<IncomingNotification> queue, CancellationToken token)
    {
        try
        {
            await foreach (var incoming in queue.ReadAllAsync(token))
            {
                if (!CanNotify(current, incoming.Resource, incoming.DeviceId, incoming.Generation)) continue;
                string? nickname = null, groupName = null;
                // Optional display metadata cannot hold up chat synchronization or block notifications indefinitely.
                using var metadataTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                metadataTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    nickname = (await current.ProfileManager.GetProfileAsync(incoming.Sender, metadataTimeout.Token))?.Nickname;
                    if (incoming.Group is { } group)
                        groupName = (await current.GroupManager.GetGroupAsync(group, metadataTimeout.Token))?.Group.Name;
                }
                catch (Exception) when (!token.IsCancellationRequested) { }
                token.ThrowIfCancellationRequested();
                if (!CanNotify(current, incoming.Resource, incoming.DeviceId, incoming.Generation)) continue;
                var culture = CultureInfo.DefaultThreadCurrentUICulture ?? CultureInfo.CurrentUICulture;
                string Text(string key) => Strings.ResourceManager.GetString(key, culture)!;
                string title = incoming.Group is null ? nickname ?? "OneGate" : groupName ?? Text("ChatNotificationGroup");
                string body = incoming.Group is not null && nickname is not null
                    ? string.Format(culture, Text("ChatNotificationSenderMessage"), nickname) : Text("ChatNotificationNewMessage");
                var data = new Dictionary<string, string>
                {
                    ["og_v"] = "1", ["og_network"] = registry.Context.ToString(), ["og_to"] = AccountId!,
                    ["og_device"] = incoming.DeviceId, ["og_kind"] = incoming.Group is null ? "direct" : "group",
                    ["og_from"] = incoming.Sender, ["og_message"] = incoming.MessageId
                };
                if (incoming.Group is { } subject) data["og_subject"] = subject.GroupId;
                try
                {
                    await DesktopNotificationService.ShowAsync(title, body, data,
                        () => CanNotify(current, incoming.Resource, incoming.DeviceId, incoming.Generation));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Push] Local notification failed: {ex.GetType().Name}.");
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
#endif
