using Meshline.Identity;
using Meshline.Models;
using Meshline.Models.Protocol;
using NeoOrder.OneGate.Pages;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services.Messaging;

namespace NeoOrder.OneGate.Services.Notifications;

/// <summary>Notification data is an untrusted navigation hint, never a message or read receipt.</summary>
public sealed class PushNavigationService(MessagingService messaging)
{
    internal static readonly string[] Keys = ["og_v", "og_network", "og_to", "og_device", "og_kind", "og_from", "og_message", "og_subject"];
    Target? pending;
    readonly HashSet<Target> handled = [];
    readonly Queue<Target> history = [];
    bool opening;
    bool foreground;

    public void Receive(IReadOnlyDictionary<string, string> values)
    {
        if (Parse(values) is not { } target) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (handled.Contains(target)) return;
            pending = target;
            TryOpen();
        });
    }

    public void SetForeground(bool visible) { foreground = visible; if (visible) TryOpen(); }
    public void TryOpen() => MainThread.BeginInvokeOnMainThread(OpenAsync);

    async void OpenAsync()
    {
        if (opening || !foreground || pending is not { } target || Shell.Current is not AppShell shell) return;
        opening = true;
        try
        {
            var identity = await messaging.GetPushNavigationIdentityAsync();
            if (!foreground || Shell.Current != shell || pending != target) return;
            if (identity is null) return; // App startup / wallet entry will notify us again.
            pending = null;
            handled.Add(target); history.Enqueue(target);
            if (history.Count > 128) handled.Remove(history.Dequeue());
            if (identity.Value.Network != target.Network || identity.Value.Account != target.Account)
            {
                await shell.GoToAsync("//home");
                await shell.DisplayAlertAsync(Strings.ChatEnableNotifications, Strings.ChatPushOtherAccount, Strings.OK);
                return;
            }
            if (identity.Value.Device != target.Device) return;
            if (target.Kind == "direct" && shell.CurrentPage is ChatPage current && current.Address == target.Sender.Split(':')[2]) return;
            await shell.GoToAsync("//home");
            if (shell.CurrentPage is HomePage home) home.ShowChatSection();
            if (target.Kind != "direct") return;
            // Account/wallet changes during navigation must not open another account's conversation.
            if (await messaging.GetPushNavigationIdentityAsync() != identity || !foreground || Shell.Current != shell) return;
            await shell.GoToAsync("chat", new ShellNavigationQueryParameters { ["address"] = target.Sender.Split(':')[2], ["pushIdentity"] = identity.Value });
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Push] Navigation failed: {ex.GetType().Name}."); }
        finally { opening = false; if (pending is { } next && next != target) TryOpen(); }
    }

    static Target? Parse(IReadOnlyDictionary<string, string> values)
    {
        string Read(string key) => values.TryGetValue(key, out string? value) ? value : "";
        if (Read("og_v") != "1" || !NetworkContext.TryParse(Read("og_network"), out var network)) return null;
        string account = Read("og_to"), sender = Read("og_from"), device = Read("og_device"), message = Read("og_message");
        if (AccountAdapter.ValidateAccountId(account) is not null || AccountAdapter.ValidateAccountId(sender) is not null
            || !account.StartsWith($"neo:{network.Reference}:", StringComparison.Ordinal)
            || !sender.StartsWith($"neo:{network.Reference}:", StringComparison.Ordinal)
            || Identifiers.ValidateDeviceId(device) is not null || Identifiers.ValidateMessageId(message) is not null) return null;
        string kind = Read("og_kind") is "direct" ? "direct" : "other";
        return new(network.ToString(), account, device, sender, message, kind);
    }

    sealed record Target(string Network, string Account, string Device, string Sender, string Message, string Kind);
}
