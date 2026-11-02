using Neo.Wallets;
using NeoOrder.OneGate.Controls.Views;
using NeoOrder.OneGate.Services.Messaging;

namespace NeoOrder.OneGate;

public partial class AppShell
{
    readonly MessagingService messagingService;
    readonly IWalletProvider walletProvider;
    CancellationTokenSource? unreadCancellation;
    Wallet? unreadWallet;
    string? unreadAddress;
    Window? unreadWindow;
    bool monitoringUnread;
    bool unreadRefreshPending;
    long unreadCount;

    void OnUnreadLoaded(object? sender, EventArgs e)
    {
        if (monitoringUnread) return;
        monitoringUnread = true;
        messagingService.Changed += OnUnreadChanged;
        messagingService.UnreadCountChanged += OnUnreadChanged;
        walletProvider.WalletChanged += OnUnreadWalletChanged;
        unreadWindow = Window;
        if (unreadWindow is not null) unreadWindow.Destroying += OnUnreadUnloaded;
        RefreshHomeUnread();
        UpdateUnreadWindowTitle();
    }

    void OnUnreadUnloaded(object? sender, EventArgs e)
    {
        monitoringUnread = false;
        messagingService.Changed -= OnUnreadChanged;
        messagingService.UnreadCountChanged -= OnUnreadChanged;
        walletProvider.WalletChanged -= OnUnreadWalletChanged;
        if (unreadWindow is not null) unreadWindow.Destroying -= OnUnreadUnloaded;
        UpdateUnreadWindowTitle();
        unreadWindow = null;
        unreadRefreshPending = false;
        unreadCancellation?.Cancel();
    }

    void OnUnreadChanged(object? sender, EventArgs e) => RefreshHomeUnread();

    void OnUnreadWalletChanged(object? sender, Wallet? wallet) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (monitoringUnread) RefreshHomeUnread();
    });

    async void RefreshHomeUnread()
    {
        if (!monitoringUnread) return;
        Wallet? wallet = walletProvider.GetWallet();
        string? address = wallet?.GetDefaultAccount()?.Address;
        if (!ReferenceEquals(wallet, unreadWallet) || address != unreadAddress)
        {
            unreadCancellation?.Cancel();
            unreadWallet = wallet;
            unreadAddress = address;
            SetUnreadCount(0);
        }
        unreadRefreshPending = true;
        if (unreadCancellation is not null) return;
        unreadRefreshPending = false;
        using var cancellation = new CancellationTokenSource();
        unreadCancellation = cancellation;
        try
        {
            var (accountId, count) = await messagingService.GetChatUnreadCountAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (monitoringUnread && ReferenceEquals(wallet, walletProvider.GetWallet())
                && address == wallet?.GetDefaultAccount()?.Address && accountId == messagingService.AccountId)
                SetUnreadCount(count);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Keep the last known count until a later change or reload retries the local read.
            System.Diagnostics.Debug.WriteLine($"[Meshline] Unread count refresh failed: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(unreadCancellation, cancellation))
            {
                unreadCancellation = null;
                if (unreadRefreshPending && monitoringUnread) RefreshHomeUnread();
            }
        }
    }

    void SetUnreadCount(long count)
    {
        if (unreadCount == count) return;
        unreadCount = count;
        Badge.SetCount(homeTab, count);
        UpdateUnreadWindowTitle();
    }

    void UpdateUnreadWindowTitle()
    {
        // This shell owns only its main window, never independent DApp windows.
        if (unreadWindow is not { } window || window.Page is AppShell shell && !ReferenceEquals(shell, this)) return;
        string title = monitoringUnread && unreadCount > 0 ? $"OneGate ({Badge.FormatCount(unreadCount)})" : "OneGate";
        if (window.Title != title) window.Title = title;
#if WINDOWS
        if (window.TitleBar is TitleBar titleBar && titleBar.Title != title) titleBar.Title = title;
#endif
    }
}
