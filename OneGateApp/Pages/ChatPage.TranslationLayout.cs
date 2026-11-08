using NeoOrder.OneGate.Models;
#if ANDROID
using RecyclerView = AndroidX.RecyclerView.Widget.RecyclerView;
#endif

namespace NeoOrder.OneGate.Pages;

public partial class ChatPage
{
    bool translationLayoutPending;
    int translationStableFrames;
    Func<bool>? restoreTranslationOffset;
#if ANDROID
    RecyclerView? translationLayoutView;
    Android.Views.ViewTreeObserver? translationLayoutObserver;
    bool translationViewportSyncRequested;
#elif IOS || MACCATALYST
    CoreAnimation.CADisplayLink? translationLayoutDisplayLink;
#endif

    void ChangeTranslationLayout(Action change)
    {
        bool explicitScrollQueued = explicitScrollRevision == scrollRevision;
        if (!translationLayoutPending)
        {
#if ANDROID
            if (!TryObserveAndroidTranslationLayout())
            {
                change();
                messageList.InvalidateMeasure();
                return;
            }
#endif
            if (!explicitScrollQueued) restoreTranslationOffset = CaptureTranslationOffset();
#if WINDOWS
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnTranslationRendering;
#elif IOS || MACCATALYST
            translationLayoutDisplayLink = CoreAnimation.CADisplayLink.Create(CompleteTranslationLayout);
            translationLayoutDisplayLink.AddToRunLoop(Foundation.NSRunLoop.Main, Foundation.NSRunLoopMode.Common);
#endif
            translationLayoutPending = true;
        }
        translationStableFrames = 0;
        // Hold the reading position until the new row heights and viewport have settled.
        if (!explicitScrollQueued)
        {
            followLatest = false;
            scrollRevision++;
            latestScrollRevision = null;
        }
        change();
        NotifyScrollState();
        messageList.InvalidateMeasure();
    }

    void CompleteTranslationLayout()
    {
        if (!translationLayoutPending) return;
        if (!isVisible || !HasLoaded || !messageList.IsLoaded) { StopTranslationLayout(); return; }
#if ANDROID
        if (!IsAndroidTranslationViewportReady()) { translationStableFrames = 0; return; }
#endif
        // Native measurements must settle before normal scroll and read tracking resumes.
        if (restoreTranslationOffset?.Invoke() == false) translationStableFrames = 0;
        else if (++translationStableFrames >= 2) StopTranslationLayout(synchronizeViewport: true);
    }

    void StopTranslationLayout(bool synchronizeViewport = false)
    {
#if ANDROID
        translationViewportSyncRequested = false;
#endif
        if (!translationLayoutPending) return;
#if WINDOWS
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnTranslationRendering;
#elif ANDROID
        messageList.HandlerChanging -= OnTranslationHandlerChanging;
        if (translationLayoutView is { } view) view.ViewDetachedFromWindow -= OnTranslationViewDetached;
        translationLayoutView = null;
        if (translationLayoutObserver is { IsAlive: true } observer) observer.PreDraw -= OnTranslationPreDraw;
        translationLayoutObserver = null;
#elif IOS || MACCATALYST
        translationLayoutDisplayLink?.Invalidate();
        translationLayoutDisplayLink?.Dispose();
        translationLayoutDisplayLink = null;
#endif
        restoreTranslationOffset = null;
        translationLayoutPending = false;
        if (synchronizeViewport) UpdateTranslationScrollState();
        else NotifyScrollState();
    }

    void UpdateTranslationScrollState()
    {
        if (!isVisible || !HasLoaded || !IsMessageListPresented) return;
        var (first, last) = GetVisibleMessageRange();
        if (last >= 0)
        {
            firstVisibleIndex = Math.Max(0, first);
            followLatest = IsAtLatestMessage(last);
            if (followLatest) { ConfirmLatestMessagesDisplayed(firstVisibleIndex); return; }
        }
        NotifyScrollState();
    }

#if WINDOWS
    void OnTranslationRendering(object? sender, object e) => CompleteTranslationLayout();
#elif ANDROID
    bool TryObserveAndroidTranslationLayout()
    {
        if (messageList.Handler?.PlatformView is not RecyclerView { IsAttachedToWindow: true } list
            || list.ViewTreeObserver is not { IsAlive: true } observer) return false;

        translationLayoutView = list;
        translationLayoutObserver = observer;
        messageList.HandlerChanging += OnTranslationHandlerChanging;
        list.ViewDetachedFromWindow += OnTranslationViewDetached;
        observer.PreDraw += OnTranslationPreDraw;
        list.PostInvalidateOnAnimation();
        return true;
    }

    bool IsAndroidTranslationViewportReady()
    {
        if (translationLayoutView is not { IsLaidOut: true, Height: > 0, IsLayoutRequested: false, HasPendingAdapterUpdates: false } list)
            return false;
        int itemCount = Messages.Count + (messageList.Header is null ? 0 : 1) + (messageList.Footer is null ? 0 : 1);
        return Messages.Count == 0 || list.GetAdapter()?.ItemCount == itemCount;
    }

    void OnTranslationHandlerChanging(object? sender, HandlerChangingEventArgs e) => InterruptAndroidTranslationLayout();

    void OnTranslationHandlerChanged(object? sender, EventArgs e)
    {
        if (translationViewportSyncRequested) OnMessagesLayoutChanged(sender, e);
    }

    void OnTranslationViewDetached(object? sender, Android.Views.View.ViewDetachedFromWindowEventArgs e) => InterruptAndroidTranslationLayout();

    void InterruptAndroidTranslationLayout()
    {
        StopTranslationLayout();
        translationViewportSyncRequested = isVisible && HasLoaded && IsMessageListPresented;
    }

    void OnTranslationPreDraw(object? sender, Android.Views.ViewTreeObserver.PreDrawEventArgs e)
    {
        e.Handled = true;
        CompleteTranslationLayout();
        translationLayoutView?.PostInvalidateOnAnimation();
    }
#endif

    Func<bool>? CaptureTranslationOffset()
    {
#if ANDROID
        if (IsAndroidTranslationViewportReady() && translationLayoutView is { } list
            && list.GetLayoutManager() is AndroidX.RecyclerView.Widget.LinearLayoutManager layout)
        {
            int headerCount = messageList.Header is null ? 0 : 1;
            int first = layout.FindFirstVisibleItemPosition();
            var anchor = Messages.ElementAtOrDefault(first - headerCount);
            bool anchoredToHeader = first == 0 && headerCount == 1;
            if ((!anchoredToHeader && anchor is null) || layout.FindViewByPosition(first) is not { } row) return null;

            int top = row.Top;
            (int Target, int First, int? Top)? attempted = null;
            return () =>
            {
                if (list.ScrollState != AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateIdle) { StopTranslationLayout(synchronizeViewport: true); return true; }
                if ((messageList.Header is null ? 0 : 1) != headerCount) return true;
                int position = 0;
                if (anchor is not null)
                {
                    int index = FindTranslationAnchorIndex(anchor);
                    if (index < 0) return true;
                    position = index + headerCount;
                }
                var current = (position, layout.FindFirstVisibleItemPosition(), layout.FindViewByPosition(position)?.Top);
                // A collapse can clamp the old offset at the content boundary.
                if (current == (position, position, top) || attempted == current) return true;
                attempted = current;
                layout.ScrollToPositionWithOffset(position, top - list.PaddingTop);
                return false;
            };
        }
#elif IOS || MACCATALYST
        if (messageList.Handler?.PlatformView is UIKit.UICollectionView list)
        {
            if (!IsTranslationCollectionReady(list)) return null;
            var first = list.IndexPathsForVisibleItems.Where(path => path.Section == 0).MinBy(path => path.Item);
            if (first is null || Messages.ElementAtOrDefault((int)first.Item) is not { } anchor
                || list.CellForItem(first) is not { } cell) return null;

            double viewportOffset = (double)cell.Frame.Y - (double)list.ContentOffset.Y;
            return () =>
            {
                if (list.Window is null || !ReferenceEquals(messageList.Handler?.PlatformView, list)) return true;
                if (list.Dragging || list.Decelerating) { StopTranslationLayout(synchronizeViewport: true); return true; }
                list.LayoutIfNeeded();
                if (!IsTranslationCollectionReady(list)) return false;
                int index = FindTranslationAnchorIndex(anchor);
                if (index < 0) return true;
                using var path = Foundation.NSIndexPath.FromItemSection(index, 0);
                if (list.GetLayoutAttributesForItem(path) is not { } attributes) return true;
                var target = new CoreGraphics.CGPoint(list.ContentOffset.X, Math.Clamp((double)attributes.Frame.Y - viewportOffset,
                    -(double)list.AdjustedContentInset.Top, GetLatestMessageOffset(list)));
                if (Math.Abs((double)(list.ContentOffset.Y - target.Y)) < 1) return true;
                list.SetContentOffset(target, false);
                return false;
            };
        }
#elif WINDOWS
        if (messageList.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.ListViewBase list
            && FindNativeScrollViewer(list) is { } scroll)
        {
            double offset = scroll.VerticalOffset;
            double? attempted = null;
            return () =>
            {
                list.UpdateLayout();
                double target = Math.Min(offset, scroll.ScrollableHeight);
                if (Math.Abs(scroll.VerticalOffset - target) < 1 || attempted == scroll.VerticalOffset) return true;
                attempted = scroll.VerticalOffset;
                scroll.ChangeView(null, target, null, true);
                return false;
            };
        }
#endif
        return null;
    }

#if ANDROID || IOS || MACCATALYST
    int FindTranslationAnchorIndex(ChatMessageViewModel anchor)
    {
        // History insertion and pending-message replacement can both move the anchor.
        var current = Messages.FirstOrDefault(message => IsSameMessage(message, anchor));
        return current is null ? -1 : Messages.IndexOf(current);
    }

    static bool IsSameMessage(ChatMessageViewModel message, ChatMessageViewModel anchor) =>
        ReferenceEquals(message, anchor)
        || (anchor.PendingId is { } pendingId && message.PendingId == pendingId)
        || (anchor.MessageId is { } messageId && message.MessageId == messageId && message.SenderAccountId == anchor.SenderAccountId);
#endif

#if IOS || MACCATALYST
    bool IsTranslationCollectionReady(UIKit.UICollectionView list) => list.Window is not null && !list.HasUncommittedUpdates
        && list.NumberOfSections() == 1 && list.NumberOfItemsInSection(0) == Messages.Count;
#endif
}
