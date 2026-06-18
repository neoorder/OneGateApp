#if ANDROID
using Android.Views;
using Android.Views.Accessibility;
using AndroidView = Android.Views.View;

namespace NeoOrder.OneGate.Services;

static class AndroidSensitiveContentProtection
{
    static readonly SensitiveAccessibilityDelegate AccessibilityDelegate = new();

    public static void Hide(VisualElement? element)
    {
        if (element is null)
            return;

        if (element.Handler is null)
        {
            element.HandlerChanged -= OnHandlerChanged;
            element.HandlerChanged += OnHandlerChanged;
            return;
        }

        Apply(element);
        MainThread.BeginInvokeOnMainThread(() => Apply(element));
    }

    static void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is not VisualElement element)
            return;

        element.HandlerChanged -= OnHandlerChanged;
        Hide(element);
    }

    static void Apply(VisualElement element)
    {
        if (element.Handler?.PlatformView is AndroidView view)
        {
            view.ImportantForAccessibility = ImportantForAccessibility.NoHideDescendants;
            view.ContentDescription = string.Empty;
            view.SetAccessibilityDelegate(AccessibilityDelegate);
        }

        foreach (Element child in GetChildren(element))
        {
            if (child is VisualElement visualChild)
                Apply(visualChild);
        }
    }

    static IEnumerable<Element> GetChildren(Element element)
    {
        if (element is Layout layout)
        {
            foreach (IView child in layout.Children)
            {
                if (child is Element childElement)
                    yield return childElement;
            }
        }

        if (element is Border border && border.Content is Element borderContent)
            yield return borderContent;

        if (element is ContentView contentView && contentView.Content is Element contentViewContent)
            yield return contentViewContent;

        if (element is ScrollView scrollView && scrollView.Content is Element scrollViewContent)
            yield return scrollViewContent;
    }

    sealed class SensitiveAccessibilityDelegate : AndroidView.AccessibilityDelegate
    {
        public override void OnInitializeAccessibilityNodeInfo(AndroidView host, AccessibilityNodeInfo info)
        {
            base.OnInitializeAccessibilityNodeInfo(host, info);
            info.Text = null;
            info.ContentDescription = null;
        }
    }
}
#endif
