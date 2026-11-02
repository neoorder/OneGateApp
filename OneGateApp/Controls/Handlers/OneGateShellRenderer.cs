#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;
using Microsoft.Maui.Platform;
using NeoOrder.OneGate.Controls.Views;
using System.Collections.Specialized;
using System.ComponentModel;
#if ANDROID
using Google.Android.Material.BottomNavigation;
using NativeColor = Android.Graphics.Color;
#else
using UIKit;
#endif

namespace NeoOrder.OneGate.Controls.Handlers;

public sealed class OneGateShellRenderer : ShellRenderer
{
    sealed class TabBadgeObserver : IDisposable
    {
        readonly Action update;
        ShellSection[] sections = [];
        public IShellItemController Item { get; }

        public TabBadgeObserver(IShellItemController item, Action update)
        {
            Item = item;
            this.update = update;
            Item.ItemsCollectionChanged += OnItemsChanged;
            ObserveSections();
        }

        void ObserveSections()
        {
            foreach (var section in sections) section.PropertyChanged -= OnPropertyChanged;
            sections = [.. Item.GetItems()];
            foreach (var section in sections) section.PropertyChanged += OnPropertyChanged;
        }

        void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ObserveSections();
            update();
        }

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == Badge.CountProperty.PropertyName) update();
        }

        public void Dispose()
        {
            Item.ItemsCollectionChanged -= OnItemsChanged;
            foreach (var section in sections) section.PropertyChanged -= OnPropertyChanged;
            sections = [];
        }
    }

#if ANDROID
    protected override IShellBottomNavViewAppearanceTracker CreateBottomNavViewAppearanceTracker(ShellItem shellItem)
        => new TabBadgeTracker(this, shellItem);

    sealed class TabBadgeTracker : ShellBottomNavViewAppearanceTracker
    {
        readonly TabBadgeObserver observer;
        BottomNavigationView? bottomView;

        public TabBadgeTracker(IShellContext context, ShellItem shellItem) : base(context, shellItem)
        {
            observer = new TabBadgeObserver(shellItem, UpdateBadges);
        }

        public override void SetAppearance(BottomNavigationView bottomView, IShellAppearanceElement appearance)
        {
            base.SetAppearance(bottomView, appearance);
            this.bottomView = bottomView;
            UpdateBadges();
        }

        public override void ResetAppearance(BottomNavigationView bottomView)
        {
            base.ResetAppearance(bottomView);
            this.bottomView = bottomView;
            UpdateBadges();
        }

        void UpdateBadges()
        {
            if (bottomView is null) return;
            var sections = observer.Item.GetItems();
            for (int index = 0; index < bottomView.Menu.Size(); index++)
            {
                int itemId = bottomView.Menu.GetItem(index)!.ItemId;
                long count = index < sections.Count ? Badge.GetCount(sections[index]) : 0;
                if (count <= 0)
                {
                    bottomView.RemoveBadge(itemId);
                    continue;
                }
                var badge = bottomView.GetOrCreateBadge(itemId);
                badge.BackgroundColor = ((Color)Application.Current!.Resources["BadgeBackground"]).ToPlatform();
                badge.BadgeTextColor = NativeColor.White;
                badge.MaxCharacterCount = 3; // Material displays counts above 99 as 99+.
                badge.SetAutoAdjustToWithinGrandparentBounds(true);
                badge.Number = (int)Math.Min(count, int.MaxValue);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                observer.Dispose();
                bottomView = null;
            }
            base.Dispose(disposing);
        }
    }
#else
    protected override IShellTabBarAppearanceTracker CreateTabBarAppearanceTracker() => new TabBadgeTracker();

    sealed class TabBadgeTracker : ShellTabBarAppearanceTracker
    {
        UITabBarController? controller;
        UITabBarItem[]? styledItems;
        TabBadgeObserver? observer;

        public override void SetAppearance(UITabBarController controller, ShellAppearance appearance)
        {
            base.SetAppearance(controller, appearance);
            this.controller = controller;
            UpdateBadges(updateAppearance: true);
        }

        public override void ResetAppearance(UITabBarController controller)
        {
            base.ResetAppearance(controller);
            this.controller = controller;
            UpdateBadges(updateAppearance: true);
        }

        public override void UpdateLayout(UITabBarController controller)
        {
            base.UpdateLayout(controller);
            this.controller = controller;
            UpdateBadges();
        }

        void UpdateBadges(bool updateAppearance = false)
        {
            if (controller is not ShellItemRenderer renderer || controller.TabBar.Items is not { } items) return;
            if (observer?.Item != renderer.ShellItemController)
            {
                observer?.Dispose();
                observer = new TabBadgeObserver(renderer.ShellItemController, () => UpdateBadges());
            }
            var sections = renderer.ShellItemController.GetItems();
            bool updateColors = updateAppearance || styledItems is null || !styledItems.SequenceEqual(items);
            for (int index = 0; index < items.Length; index++)
            {
                var item = items[index];
                long count = index < sections.Count ? Badge.GetCount(sections[index]) : 0;
                string? text = Badge.FormatCount(count);
                // Layout callbacks must not request another layout for an unchanged badge.
                if (item.BadgeValue != text) item.BadgeValue = text;
                if (updateColors)
                {
                    item.BadgeColor = ((Color)Application.Current!.Resources["BadgeBackground"]).ToPlatform();
                    var attributes = new UIStringAttributes { ForegroundColor = UIColor.White };
                    item.SetBadgeTextAttributes(attributes, UIControlState.Normal);
                    item.SetBadgeTextAttributes(attributes, UIControlState.Selected);
                }
            }
            styledItems = items;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                observer?.Dispose();
                observer = null;
                controller = null;
                styledItems = null;
            }
            base.Dispose(disposing);
        }
    }
#endif
}
#endif
