using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using System.Globalization;
#if ANDROID
using Microsoft.Maui.Controls.Handlers.Items;
#elif WINDOWS
using Microsoft.UI.Xaml.Media;
using NativeListView = Microsoft.UI.Xaml.Controls.ListViewBase;
using NativeScrollViewer = Microsoft.UI.Xaml.Controls.ScrollViewer;
#endif

namespace NeoOrder.OneGate.Pages;

public partial class ChatPage
{
    int newMessageCount;
    long? firstNewMessageSequence;
    int? latestScrollRevision;
    int? explicitScrollRevision;
    int? explicitBottomScrollRevision;
    ChatMessageViewModel? initialScrollAnchor;
    int scrollRevision;
#if WINDOWS
    (double ExtentHeight, double ViewportHeight)? lastScrollLayout;
#elif ANDROID
    Android.Views.ViewTreeObserver? initialPresentationObserver;
    Android.Views.ViewTreeObserver? latestMessageScrollObserver;
    AndroidX.RecyclerView.Widget.RecyclerView? latestMessageScrollList;
    int latestMessageScrollRevision;
#elif IOS || MACCATALYST
    CoreAnimation.CADisplayLink? initialPresentationDisplayLink;
#endif
    bool initialPresentationQueued;

    public bool IsMessageListPresented
    {
        get;
        private set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    }

    void OnMessagesLayoutChanged(object? sender, EventArgs e)
    {
        if (translationLayoutPending) { translationStableFrames = 0; return; }
#if ANDROID
        if (translationViewportSyncRequested && isVisible && HasLoaded && IsMessageListPresented && TryObserveAndroidTranslationLayout())
        {
            translationViewportSyncRequested = false;
            translationLayoutPending = true;
            translationStableFrames = 0;
            return;
        }
#endif
        if (!IsMessageListPresented) EnsureInitialMessagePresentation();
        // Row remeasurement can move the bottom without resizing the outer list.
        else QueueLatestMessageScroll();
    }

    void EnsureInitialMessagePresentation()
    {
        if (!IsMessageListPresented && !initialPresentationQueued && isVisible && HasLoaded && !IsLoading
            && messageList.IsLoaded && messageList.Width > 0 && messageList.Height > 0)
            StartInitialMessagePresentation();
    }

    void QueueLatestMessageScroll()
    {
        if (translationLayoutPending || explicitScrollRevision == scrollRevision) return;
        if (!IsMessageListPresented || !isVisible || !HasLoaded || !followLatest || Messages.Count == 0
            || !messageList.IsLoaded || messageList.Width <= 0 || messageList.Height <= 0
            || latestScrollRevision == scrollRevision) return;
        int revision = scrollRevision;
        latestScrollRevision = revision;
        Dispatcher.Dispatch(() =>
        {
            if (latestScrollRevision != revision) return;
            latestScrollRevision = null;
            if (revision == scrollRevision && IsMessageListPresented && isVisible && HasLoaded && followLatest
                && messageList.IsLoaded && messageList.Width > 0 && messageList.Height > 0
                && Messages.LastOrDefault() is { } target)
                ScrollMessageIntoView(target, scrollToEnd: true);
        });
    }

    void ResetMessagePresentation()
    {
        scrollRevision++;
        latestScrollRevision = null;
        initialScrollAnchor = null;
        StopInitialMessagePresentation();
#if ANDROID
        StopLatestMessageScrollConfirmation();
#endif
        IsMessageListPresented = false;
#if WINDOWS
        lastScrollLayout = null;
#endif
    }

    public bool ShowLatestMessages => IsMessageListPresented && isVisible && IsChatEnabled && HasLoaded && Messages.Count > 0
        && !translationLayoutPending && !followLatest;
    public string LatestMessagesText => newMessageCount > 0
        ? string.Format(Strings.ChatNewMessages, newMessageCount > 99 ? "99+" : newMessageCount.ToString(CultureInfo.CurrentCulture))
        : Strings.ChatBackToLatest;

    async void OnMessagesScrolled(object sender, ItemsViewScrolledEventArgs e)
    {
        if (translationLayoutPending) return;
        if (!IsMessageListPresented || !isVisible || !HasLoaded || Messages.Count == 0 || e.LastVisibleItemIndex < 0) return;
        bool atEnd = IsAtLatestMessage(e.LastVisibleItemIndex);
        bool layoutChanged = false;
#if WINDOWS
        layoutChanged = UpdateScrollLayout();
        // Resizing or remeasuring rows changes the extent without a user scroll.
        if (layoutChanged && followLatest && !atEnd)
        {
            QueueLatestMessageScroll();
            return;
        }
#endif
        if (e.VerticalDelta < 0 && !layoutChanged)
        {
            CancelQuoteNavigation();
            if (!atEnd)
            {
                // Browsing upward cancels queued layout following and message-refresh scrolling.
                scrollRevision++;
                latestScrollRevision = null;
            }
        }
        if (!atEnd && latestScrollRevision == scrollRevision) return;
        if (atEnd)
        {
            latestScrollRevision = null;
            explicitBottomScrollRevision = null;
        }
        firstVisibleIndex = Math.Max(0, e.FirstVisibleItemIndex);
        followLatest = atEnd;
        if (followLatest) ClearNewMessageHint();
        else if (!IsChatContentCovered && e.FirstVisibleItemIndex >= 0 && firstNewMessageSequence is { } sequence)
        {
            var firstNew = Messages.FirstOrDefault(message => !message.IsPending && message.LocalSequence == sequence);
            int index = firstNew is null ? -1 : Messages.IndexOf(firstNew);
            // This hint points to the start of new arrivals; seeing it does not mark the whole chat read.
            if (index >= e.FirstVisibleItemIndex && index <= e.LastVisibleItemIndex) ClearNewMessageHint();
        }
        NotifyScrollState();
        MarkDisplayedMessagesRead(atLatest: atEnd);
        // Only an upward movement near the start loads another local page. Errors require explicit retry.
        if (e.VerticalDelta < 0 && e.FirstVisibleItemIndex is >= 0 and <= 2 && !followLatest && EarlierMessagesError is null)
            await LoadEarlierAsync();
    }

    void RecordNewMessages(IEnumerable<ChatMessageViewModel> messages)
    {
        if (!HasLoaded || followLatest) return;
        foreach (var message in messages.Where(message => !message.IsOutgoing && !message.IsPending))
        {
            firstNewMessageSequence ??= message.LocalSequence;
            newMessageCount = Math.Min(100, newMessageCount + 1);
        }
    }

    void ClearNewMessageHint()
    {
        newMessageCount = 0;
        firstNewMessageSequence = null;
    }

    void OnLatestMessagesClicked(object sender, EventArgs e)
    {
        if (!ShowLatestMessages || IsChatContentCovered || Messages.LastOrDefault() is not { } target) return;
        CancelQuoteNavigation();
        ClearQuoteHighlight();
        ClearQuoteReturnPosition();
        if (firstNewMessageSequence is { } sequence
            && Messages.FirstOrDefault(message => !message.IsPending && message.LocalSequence == sequence) is { } firstNew)
        {
            FocusQuotedMessage(firstNew);
            return;
        }
        // Wait for the viewport to reach the end before clearing the hint or marking messages read.
        RequestBottomScroll(target);
    }

    void RequestBottomScroll(ChatMessageViewModel target)
    {
        restoreTranslationOffset = null;
        explicitBottomScrollRevision = ++scrollRevision;
        latestScrollRevision = null;
        DispatchMessageScroll(target, scrollToEnd: true, explicitScroll: true);
    }

    void DispatchMessageScroll(ChatMessageViewModel target, bool scrollToEnd, bool explicitScroll = false)
    {
        int revision = scrollRevision;
        if (explicitScroll) explicitScrollRevision = revision;
        bool dispatched = Dispatcher.Dispatch(() =>
        {
            if (explicitScroll && explicitScrollRevision == revision) explicitScrollRevision = null;
            if (!IsMessageListPresented || !isVisible || revision != scrollRevision) return;
            if (explicitScroll && scrollToEnd)
            {
                // Resolve the latest bubble after any updates that arrived while the scroll was queued.
                if (Messages.LastOrDefault() is not { } latest) return;
                StopTranslationLayout();
                ScrollMessageIntoView(latest, scrollToEnd: true);
            }
            else if (Messages.Contains(target)) ScrollMessageIntoView(target, scrollToEnd);
        });
        if (!dispatched && explicitScroll) explicitScrollRevision = null;
    }

    void ScrollMessageIntoView(ChatMessageViewModel target, bool scrollToEnd)
    {
#if WINDOWS
        if (scrollToEnd)
        {
            if (messageList.Handler?.PlatformView is not NativeListView { IsLoaded: true } list || list.Items.Count == 0) return;
            // MAUI's End alignment applies an extra upward adjustment after ScrollIntoView has already
            // been clamped at the bottom. Realize the last row, then use the actual scroll extent instead.
            list.UpdateLayout();
            list.ScrollIntoView(list.Items[list.Items.Count - 1]);
            list.UpdateLayout();
            if (FindNativeScrollViewer(list) is { ViewportHeight: > 0 } scroll)
                scroll.ChangeView(null, scroll.ScrollableHeight, null, disableAnimation: true);
            // A short list can already fit without producing a scroll event.
            if (isVisible && HasLoaded && list.Items.Count == Messages.Count
                && list.ItemsPanelRoot is Microsoft.UI.Xaml.Controls.ItemsStackPanel panel && IsAtLatestMessage(panel.LastVisibleIndex))
                ConfirmLatestMessagesDisplayed(panel.FirstVisibleIndex);
            return;
        }
#elif ANDROID
        // After the first layout a short list may already fit, without another scroll event.
        if (scrollToEnd && isVisible && HasLoaded && Messages.Count > 0
            && messageList.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView
                { IsLaidOut: true, Height: > 0, IsLayoutRequested: false, HasPendingAdapterUpdates: false } list
            && list.GetLayoutManager() is AndroidX.RecyclerView.Widget.LinearLayoutManager layout)
        {
            int headerCount = messageList.Header is null ? 0 : 1;
            int itemCount = Messages.Count + headerCount + (messageList.Footer is null ? 0 : 1);
            if (list.GetAdapter()?.ItemCount == itemCount && IsAtLatestMessage(layout.FindLastVisibleItemPosition() - headerCount))
            {
                ConfirmLatestMessagesDisplayed(layout.FindFirstVisibleItemPosition() - headerCount);
                return;
            }
        }
#endif
        messageList.ScrollTo(target, position: scrollToEnd ? ScrollToPosition.End : ScrollToPosition.Start, animate: false);
#if ANDROID
        if (scrollToEnd && messageList.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView nativeList)
        {
            StopLatestMessageScrollConfirmation();
            if (nativeList.ViewTreeObserver is not { IsAlive: true } observer) return;
            latestMessageScrollList = nativeList;
            latestMessageScrollObserver = observer;
            latestMessageScrollRevision = scrollRevision;
            // Scrolled and animation callbacks can precede the layout that updates the scroll range.
            observer.PreDraw += OnLatestMessageScrollPreDraw;
            nativeList.PostInvalidateOnAnimation();
        }
#endif
    }

#if ANDROID
    void OnLatestMessageScrollPreDraw(object? sender, Android.Views.ViewTreeObserver.PreDrawEventArgs e)
    {
        e.Handled = true;
        var list = latestMessageScrollList;
        if (list is null || latestMessageScrollRevision != scrollRevision || translationLayoutPending
            || !isVisible || !HasLoaded || !IsMessageListPresented || IsChatContentCovered
            || !ReferenceEquals(messageList.Handler?.PlatformView, list))
        {
            StopLatestMessageScrollConfirmation();
            return;
        }
        if (list.IsLayoutRequested || list.HasPendingAdapterUpdates) { list.PostInvalidateOnAnimation(); return; }
        StopLatestMessageScrollConfirmation();
        int headerCount = messageList.Header is null ? 0 : 1;
        int itemCount = Messages.Count + headerCount + (messageList.Footer is null ? 0 : 1);
        if (list.GetAdapter()?.ItemCount == itemCount
            && list.GetLayoutManager() is AndroidX.RecyclerView.Widget.LinearLayoutManager layout
            && IsAtLatestMessage(layout.FindLastVisibleItemPosition() - headerCount))
            ConfirmLatestMessagesDisplayed(layout.FindFirstVisibleItemPosition() - headerCount);
    }

    void StopLatestMessageScrollConfirmation()
    {
        if (latestMessageScrollObserver is { IsAlive: true } observer) observer.PreDraw -= OnLatestMessageScrollPreDraw;
        latestMessageScrollObserver = null;
        latestMessageScrollList = null;
    }
#endif

    void ConfirmLatestMessagesDisplayed(int firstVisibleItemIndex)
    {
        if (!IsMessageListPresented) return;
        latestScrollRevision = null;
        explicitBottomScrollRevision = null;
        firstVisibleIndex = Math.Max(0, firstVisibleItemIndex);
        followLatest = true;
#if WINDOWS
        UpdateScrollLayout();
#endif
        ClearNewMessageHint();
        NotifyScrollState();
        MarkDisplayedMessagesRead(atLatest: true);
    }

    bool IsLastMessageVisible()
    {
        if (!IsMessageListPresented || !messageList.IsLoaded || Messages.Count == 0) return false;
#if ANDROID
        if (messageList.Handler?.PlatformView is not AndroidX.RecyclerView.Widget.RecyclerView { IsAttachedToWindow: true } list)
            return false;

        var latest = Messages[^1];
        using var viewport = new Android.Graphics.Rect();
        using var bounds = new Android.Graphics.Rect();
        if (!list.GetGlobalVisibleRect(viewport)) return false;
        // A prepend can change the adapter indexes before the visible rows are laid out again.
        for (int i = 0; i < list.ChildCount; i++)
        {
            if (list.GetChildAt(i) is { IsShown: true, Alpha: > 0 } row
                && list.GetChildViewHolder(row) is TemplatedItemViewHolder { View.BindingContext: ChatMessageViewModel message }
                && IsSameMessage(message, latest) && row.GetGlobalVisibleRect(bounds)
                && Android.Graphics.Rect.Intersects(viewport, bounds))
                return true;
        }
        return false;
#else
        var (first, last) = GetVisibleMessageRange();
        int latest = Messages.Count - 1;
        return first >= 0 && first <= latest && latest <= last;
#endif
    }

    (int First, int Last) GetVisibleMessageRange()
    {
#if ANDROID
        if (messageList.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView list
            && list.GetLayoutManager() is AndroidX.RecyclerView.Widget.LinearLayoutManager layout)
        {
            int headerCount = messageList.Header is null ? 0 : 1;
            return (Math.Max(0, layout.FindFirstVisibleItemPosition() - headerCount), layout.FindLastVisibleItemPosition() - headerCount);
        }
#elif IOS || MACCATALYST
        if (messageList.Handler?.PlatformView is UIKit.UICollectionView list)
        {
            var visible = list.IndexPathsForVisibleItems.Where(path => path.Section == 0).Select(path => (int)path.Item).ToArray();
            if (visible.Length > 0) return (visible.Min(), visible.Max());
        }
#elif WINDOWS
        if (messageList.Handler?.PlatformView is NativeListView list
            && list.ItemsPanelRoot is Microsoft.UI.Xaml.Controls.ItemsStackPanel panel)
            return (panel.FirstVisibleIndex, panel.LastVisibleIndex);
#endif
        return (-1, -1);
    }

    bool IsAtLatestMessage(int lastVisibleItemIndex)
    {
        if (lastVisibleItemIndex < Messages.Count - 1) return false;
#if WINDOWS
        // A partly visible final bubble does not mean the scrollbar is at the bottom.
        return messageList.Handler?.PlatformView is NativeListView list
            && FindNativeScrollViewer(list) is { ViewportHeight: > 0 } scroll
            && scroll.ScrollableHeight - scroll.VerticalOffset <= 1;
#elif ANDROID
        // The final row may be taller than the viewport; seeing its top is not reaching its end.
        return messageList.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView { IsLaidOut: true, Height: > 0 } list
            && !list.CanScrollVertically(1);
#elif IOS || MACCATALYST
        return messageList.Handler?.PlatformView is UIKit.UICollectionView list && list.Bounds.Height > 0
            && GetLatestMessageOffset(list) - (double)list.ContentOffset.Y <= 1;
#else
        return true;
#endif
    }

    void StartInitialMessagePresentation()
    {
#if WINDOWS
        CompositionTarget.Rendering += OnInitialMessagesRendering;
#elif ANDROID
        if (messageList.Handler?.PlatformView is not AndroidX.RecyclerView.Widget.RecyclerView list
            || list.ViewTreeObserver is not { IsAlive: true } observer
            || list.GetLayoutManager() is not AndroidX.RecyclerView.Widget.LinearLayoutManager layout) return;
        initialPresentationObserver = observer;
        observer.PreDraw += OnInitialMessagesPreDraw;
        if (Messages.Count > 0)
        {
            var anchor = initialScrollAnchor ?? (followLatest ? null : Messages.ElementAtOrDefault(firstVisibleIndex));
            int index = anchor is null ? Messages.Count - 1 : Messages.IndexOf(anchor);
            // Request the initial anchor for the next layout, before its rows are drawn.
            layout.ScrollToPositionWithOffset(index + (messageList.Header is null ? 0 : 1), 0);
        }
        list.Invalidate();
#elif IOS || MACCATALYST
        initialPresentationDisplayLink = CoreAnimation.CADisplayLink.Create(PresentInitialMessages);
        initialPresentationDisplayLink.AddToRunLoop(Foundation.NSRunLoop.Main, Foundation.NSRunLoopMode.Common);
#endif
        initialPresentationQueued = true;
    }

#if WINDOWS
    void OnInitialMessagesRendering(object? sender, object e) => PresentInitialMessages();
#elif ANDROID
    void OnInitialMessagesPreDraw(object? sender, Android.Views.ViewTreeObserver.PreDrawEventArgs e)
    {
        // Let the rest of the page draw while the transparent list finishes positioning.
        e.Handled = true;
        PresentInitialMessages();
        if (initialPresentationQueued && messageList.Handler?.PlatformView is Android.Views.View view)
            view.PostInvalidateOnAnimation();
    }
#endif

    void PresentInitialMessages()
    {
        if (!isVisible || IsMessageListPresented)
        {
            StopInitialMessagePresentation();
            return;
        }
        // The initial load also supplies reply previews and send states that affect row heights.
        if (IsLoading || !HasLoaded) return;
        if (!messageList.IsLoaded || messageList.Width <= 0 || messageList.Height <= 0)
        {
            StopInitialMessagePresentation();
            return;
        }
        // Details and explicit quote jumps restore their reading position instead of the bottom.
        var anchor = initialScrollAnchor ?? (followLatest ? null : Messages.ElementAtOrDefault(firstVisibleIndex));
        int anchorIndex = anchor is null ? -1 : Messages.IndexOf(anchor);
        int first = 0, last = -1;
#if WINDOWS
        if (messageList.Handler?.PlatformView is not NativeListView { IsLoaded: true } list) return;
        list.UpdateLayout();
        if (list.Items.Count != Messages.Count) return;
        if (Messages.Count > 0)
        {
            if (anchor is null)
                ScrollMessageIntoView(Messages[^1], scrollToEnd: true);
            else
            {
                if (anchorIndex < 0) return;
                list.ScrollIntoView(list.Items[anchorIndex], Microsoft.UI.Xaml.Controls.ScrollIntoViewAlignment.Leading);
                list.UpdateLayout();
            }
            if (list.ItemsPanelRoot is not Microsoft.UI.Xaml.Controls.ItemsStackPanel panel) return;
            first = panel.FirstVisibleIndex;
            last = panel.LastVisibleIndex;
        }
#elif ANDROID
        if (messageList.Handler?.PlatformView is not AndroidX.RecyclerView.Widget.RecyclerView
                { IsLaidOut: true, Height: > 0, IsLayoutRequested: false, HasPendingAdapterUpdates: false } list
            || list.GetLayoutManager() is not AndroidX.RecyclerView.Widget.LinearLayoutManager layout) return;
        if (Messages.Count > 0)
        {
            int headerCount = messageList.Header is null ? 0 : 1;
            int itemCount = Messages.Count + headerCount + (messageList.Footer is null ? 0 : 1);
            if (list.GetAdapter()?.ItemCount != itemCount) return;
            first = layout.FindFirstVisibleItemPosition() - headerCount;
            last = layout.FindLastVisibleItemPosition() - headerCount;
            if (anchor is null && !IsAtLatestMessage(last))
            {
                if (layout.FindViewByPosition(itemCount - 1) is not { } end)
                    layout.ScrollToPositionWithOffset(itemCount - 1, 0);
                else
                    list.ScrollBy(0, layout.GetDecoratedBottom(end) - (list.Height - list.PaddingBottom));
                return; // Confirm the applied offset at the next pre-draw, after layout has settled.
            }
            if (anchor is not null && anchorIndex >= 0 && (anchorIndex < first || anchorIndex > last))
            {
                layout.ScrollToPositionWithOffset(anchorIndex + headerCount, 0);
                return;
            }
        }
#elif IOS || MACCATALYST
        if (messageList.Handler?.PlatformView is not UIKit.UICollectionView list
            || list.Window is null || list.Bounds.Height <= 0 || list.HasUncommittedUpdates) return;
        list.LayoutIfNeeded();
        if (Messages.Count > 0)
        {
            // This ungrouped list has one section; UIKit headers are supplementary views, not rows.
            if (list.NumberOfSections() != 1 || list.NumberOfItemsInSection(0) != Messages.Count) return;
            int targetIndex = anchor is null ? Messages.Count - 1 : anchorIndex;
            if (targetIndex < 0) return;
            using var target = Foundation.NSIndexPath.FromItemSection(targetIndex, 0);
            list.ScrollToItem(target, anchor is null ? UIKit.UICollectionViewScrollPosition.Bottom
                : UIKit.UICollectionViewScrollPosition.Top, animated: false);
            list.LayoutIfNeeded();
            if (anchor is null)
            {
                list.SetContentOffset(new CoreGraphics.CGPoint(list.ContentOffset.X, GetLatestMessageOffset(list)), animated: false);
                list.LayoutIfNeeded();
            }
            var visible = list.IndexPathsForVisibleItems.Where(path => path.Section == 0).Select(path => (int)path.Item).ToArray();
            if (visible.Length == 0 || list.HasUncommittedUpdates) return;
            first = visible.Min();
            last = visible.Max();
        }
#endif
        if (Messages.Count > 0)
        {
            bool positioned = anchor is null ? IsAtLatestMessage(last)
                : anchorIndex >= first && anchorIndex <= last;
            if (!positioned) return;
        }
        StopInitialMessagePresentation();
        initialScrollAnchor = null;
        firstVisibleIndex = Math.Max(0, first);
        IsMessageListPresented = true;
        if (Messages.Count > 0 && IsAtLatestMessage(last)) ConfirmLatestMessagesDisplayed(first);
        else NotifyScrollState();
    }

    void StopInitialMessagePresentation()
    {
        if (!initialPresentationQueued) return;
#if WINDOWS
        CompositionTarget.Rendering -= OnInitialMessagesRendering;
#elif ANDROID
        if (initialPresentationObserver is { IsAlive: true } observer) observer.PreDraw -= OnInitialMessagesPreDraw;
        initialPresentationObserver = null;
#elif IOS || MACCATALYST
        initialPresentationDisplayLink?.Invalidate();
        initialPresentationDisplayLink?.Dispose();
        initialPresentationDisplayLink = null;
#endif
        initialPresentationQueued = false;
    }

#if IOS || MACCATALYST
    static double GetLatestMessageOffset(UIKit.UICollectionView list) => Math.Max(-(double)list.AdjustedContentInset.Top,
        (double)list.ContentSize.Height - (double)list.Bounds.Height + (double)list.AdjustedContentInset.Bottom);
#endif

#if WINDOWS
    bool UpdateScrollLayout()
    {
        if (messageList.Handler?.PlatformView is not NativeListView list
            || FindNativeScrollViewer(list) is not { ViewportHeight: > 0 } scroll) return false;
        bool changed = lastScrollLayout is { } previous
            && (Math.Abs(previous.ExtentHeight - scroll.ExtentHeight) > 1
                || Math.Abs(previous.ViewportHeight - scroll.ViewportHeight) > 1);
        lastScrollLayout = (scroll.ExtentHeight, scroll.ViewportHeight);
        return changed;
    }

    static NativeScrollViewer? FindNativeScrollViewer(Microsoft.UI.Xaml.DependencyObject parent)
    {
        if (parent is NativeScrollViewer scroll) return scroll;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindNativeScrollViewer(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }
#endif

    void NotifyScrollState()
    {
        OnPropertyChanged(nameof(ShowQuoteReturn));
        OnPropertyChanged(nameof(ShowLatestMessages));
        OnPropertyChanged(nameof(LatestMessagesText));
    }
}
