#if WINDOWS
using NeoOrder.OneGate.Controls.Views;
#endif

namespace NeoOrder.OneGate.Pages;

public partial class ChatPage
{
    CancellationTokenSource? otherUnreadCancellation;
    bool otherUnreadRefreshPending;
    public long OtherUnreadCount
    {
        get;
        private set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowOtherUnread));
        }
    }
    Shell? unreadShell;
    public bool ShowOtherUnread => isVisible && IsChatEnabled && !UseConversationPane
        && unreadShell?.FlyoutBehavior != FlyoutBehavior.Locked && OtherUnreadCount > 0;

    void ObserveUnreadNavigation()
    {
        PropertyChanged += OnUnreadVisibilityChanged;
        unreadShell = Window?.Page as Shell;
        unreadShell?.PropertyChanged += OnUnreadNavigationChanged;
        OnPropertyChanged(nameof(ShowOtherUnread));
    }

    void StopObservingUnreadNavigation()
    {
        PropertyChanged -= OnUnreadVisibilityChanged;
        unreadShell?.PropertyChanged -= OnUnreadNavigationChanged;
        unreadShell = null;
    }

    void OnNavigationTitleLoaded(object? sender, EventArgs e) => UpdateNavigationTitleSpacing();

    void OnUnreadVisibilityChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShowOtherUnread)) UpdateNavigationTitleSpacing();
    }

    void UpdateNavigationTitleSpacing()
    {
#if ANDROID
        if (Shell.GetTitleView(this)?.Handler?.PlatformView is Android.Views.View title
            && title.Parent is Android.Views.View container)
        {
            // Move the native title container, so the badge stays inside its drawing and touch bounds.
            // The native Back button retains its original position and full touch target.
            float density = container.Resources?.DisplayMetrics?.Density ?? 1;
            container.TranslationX = ShowOtherUnread ? -24 * density : 0;
        }
#endif
    }

    void OnUnreadNavigationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == Shell.FlyoutBehaviorProperty.PropertyName)
        {
            OnPropertyChanged(nameof(ShowOtherUnread));
            RefreshOtherUnread();
        }
    }

#if WINDOWS
    TitleBar? unreadTitleBar;
    Grid? unreadTitleBarContent;

    void AttachTitleBarUnread()
    {
        if (Window?.TitleBar is not TitleBar titleBar) return;
        var badge = new Badge { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        badge.SetBinding(Badge.CountProperty, static (ChatPage page) => page.OtherUnreadCount, source: this);
        var content = new Grid { MinimumWidthRequest = 36, HeightRequest = 32, Padding = new Thickness(4, 0) };
        content.Children.Add(badge);
        content.GestureRecognizers.Add(new TapGestureRecognizer { Command = BackCommand });
        content.SetBinding(IsVisibleProperty, static (ChatPage page) => page.ShowOtherUnread, source: this);
        // The leading slot follows the native Back button; its position and behavior stay unchanged.
        unreadTitleBar = titleBar;
        unreadTitleBarContent = content;
        titleBar.LeadingContent = content;
    }

    void DetachTitleBarUnread()
    {
        if (unreadTitleBar is not null && ReferenceEquals(unreadTitleBar.LeadingContent, unreadTitleBarContent))
            unreadTitleBar.LeadingContent = null;
        unreadTitleBar = null;
        unreadTitleBarContent = null;
    }
#endif

    void OnOtherUnreadChanged(object? sender, EventArgs e) => RefreshOtherUnread();

    async void RefreshOtherUnread()
    {
        var wallet = walletProvider.GetWallet();
        string? accountId = localAccountId;
        string address = Address;
        bool IsCurrent() => !UseConversationPane && unreadShell?.FlyoutBehavior != FlyoutBehavior.Locked
            && IsCurrentMessageContext(wallet, accountId, address);
        if (!IsCurrent())
        {
            ClearOtherUnread();
            return;
        }
        otherUnreadRefreshPending = true;
        if (otherUnreadCancellation is not null) return;
        otherUnreadRefreshPending = false;
        using var cancellation = new CancellationTokenSource();
        otherUnreadCancellation = cancellation;
        try
        {
            // Shares the home badge's cached snapshot; no remote query or per-conversation history load.
            var result = await messagingService.GetChatUnreadCountAsync(cancellation.Token, excludingAddress: address);
            cancellation.Token.ThrowIfCancellationRequested();
            if (IsCurrent() && result.LocalAccountId == accountId) OtherUnreadCount = result.Count;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Keep the last known count; the next local change retries the read.
            System.Diagnostics.Debug.WriteLine($"[Meshline] Other chat unread count failed: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(otherUnreadCancellation, cancellation))
            {
                otherUnreadCancellation = null;
                if (otherUnreadRefreshPending && IsCurrent()) RefreshOtherUnread();
            }
        }
    }

    void ClearOtherUnread()
    {
        otherUnreadCancellation?.Cancel();
        otherUnreadCancellation = null;
        otherUnreadRefreshPending = false;
        OtherUnreadCount = 0;
    }
}
