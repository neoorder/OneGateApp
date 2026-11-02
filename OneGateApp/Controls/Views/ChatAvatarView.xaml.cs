using NeoOrder.OneGate.Services.Messaging;
using System.Globalization;
using System.ComponentModel;

namespace NeoOrder.OneGate.Controls.Views;

public partial class ChatAvatarView : Border
{
    public static readonly BindableProperty AddressProperty = BindableProperty.Create(nameof(Address), typeof(string), typeof(ChatAvatarView), null, propertyChanged: Changed);
    public static readonly BindableProperty DisplayNameProperty = BindableProperty.Create(nameof(DisplayName), typeof(string), typeof(ChatAvatarView), null, propertyChanged: Changed);
    public static readonly BindableProperty IsSelfProperty = BindableProperty.Create(nameof(IsSelf), typeof(bool), typeof(ChatAvatarView), false, propertyChanged: Changed);
    public string? Address { get => (string?)GetValue(AddressProperty); set => SetValue(AddressProperty, value); }
    public string? DisplayName { get => (string?)GetValue(DisplayNameProperty); set => SetValue(DisplayNameProperty, value); }
    public bool IsSelf { get => (bool)GetValue(IsSelfProperty); set => SetValue(IsSelfProperty, value); }
    MessagingService? messaging;
    Page? page;
    IDispatcherTimer? timer;
    string? identity;
    bool loaded;
    readonly List<VisualElement> visibilityParents = [];
    readonly List<ScrollView> scrollParents = [];

    public ChatAvatarView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) =>
        {
            Initial.FontSize = Math.Max(14, Width * .4);
            DefaultIcon.WidthRequest = DefaultIcon.HeightRequest = Math.Min(Width, Height) * .6;
            Refresh();
        };
    }

    static void Changed(BindableObject sender, object oldValue, object newValue) => ((ChatAvatarView)sender).Refresh();

    void OnLoaded(object? sender, EventArgs e)
    {
        if (loaded) return;
        loaded = true;
        messaging = Handler?.MauiContext?.Services.GetService<MessagingService>();
        if (messaging is not null)
        {
            messaging.ChatProfilesChanged += OnProfilesChanged;
            messaging.Changed += OnMessagingChanged;
        }
        for (Element? parent = Parent; parent is not null; parent = parent.Parent)
            if (parent is Page ancestor) { page = ancestor; break; }
        if (page is not null)
        {
            page.Appearing += OnAppearing;
            page.Disappearing += OnDisappearing;
        }
        for (Element? parent = this; parent is not null; parent = parent.Parent)
            if (parent is VisualElement visual)
            {
                visibilityParents.Add(visual);
                visual.PropertyChanged += OnVisibilityChanged;
                if (visual is ScrollView scroll) { scrollParents.Add(scroll); scroll.Scrolled += OnScrolled; }
            }
        timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMinutes(5);
        timer.Tick += OnProfilesChanged;
        timer.Start();
        Refresh();
    }

    void OnUnloaded(object? sender, EventArgs e)
    {
        loaded = false;
        timer?.Stop();
        if (timer is not null) timer.Tick -= OnProfilesChanged;
        timer = null;
        if (messaging is not null)
        {
            messaging.ChatProfilesChanged -= OnProfilesChanged;
            messaging.Changed -= OnMessagingChanged;
        }
        if (page is not null) { page.Appearing -= OnAppearing; page.Disappearing -= OnDisappearing; }
        page = null;
        foreach (var parent in visibilityParents) parent.PropertyChanged -= OnVisibilityChanged;
        visibilityParents.Clear();
        foreach (var scroll in scrollParents) scroll.Scrolled -= OnScrolled;
        scrollParents.Clear();
        ResetImage();
        identity = null;
    }

    void OnAppearing(object? sender, EventArgs e) { timer?.Start(); Refresh(force: true); }
    void OnDisappearing(object? sender, EventArgs e) { timer?.Stop(); ResetImage(); }
    void OnVisibilityChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IsVisible)) return;
        if (!IsShown()) ResetImage();
        else Refresh(force: true);
    }
    void OnScrolled(object? sender, ScrolledEventArgs e)
    {
        if (IsShown()) Refresh();
        else ResetImage();
    }
    void OnProfilesChanged(object? sender, EventArgs e) => Refresh();
    void OnMessagingChanged(object? sender, MessagingChangedEventArgs e) => Refresh();

    bool IsShown()
    {
        if (!loaded || !(timer?.IsRunning ?? false) || Width <= 0 || Height <= 0) return false;
        double x = 0, y = 0;
        for (Element? current = this; current is not null; current = current.Parent)
        {
            if (current is not VisualElement visual) continue;
            if (!visual.IsVisible) return false;
            if (visual is ScrollView scroll)
            {
                x -= scroll.ScrollX; y -= scroll.ScrollY;
                if (x + Width <= 0 || y + Height <= 0 || x >= scroll.Width || y >= scroll.Height) return false;
            }
            x += visual.X; y += visual.Y;
        }
        return true;
    }

    void Refresh(bool force = false)
    {
        if (Initial is null) return;
        string? name = DisplayName;
        string? account = messaging?.AccountId;
        string? address = IsSelf ? messaging?.Address : Address?.Split(':').Last();
        string key = account + "\n" + address;
        if (identity != key) { identity = key; ResetImage(); }
        var profile = account is not null && address is not null ? messaging?.GetChatDisplayProfile(account, address) : null;
        name = string.IsNullOrWhiteSpace(name) ? profile?.Nickname : name;
        Initial.Text = string.IsNullOrWhiteSpace(name) ? null : StringInfo.GetNextTextElement(name).ToUpperInvariant();
        DefaultIcon.IsVisible = Initial.Text is null;
        Initial.IsVisible = Initial.Text is not null;
        if (!IsShown() || messaging?.IsEnabled != true || account is null || string.IsNullOrEmpty(address)) return;
        _ = messaging.RefreshChatDisplayProfileAsync(account, address, force);
    }

    void ResetImage()
    {
        Initial.IsVisible = Initial.Text is not null;
        DefaultIcon.IsVisible = Initial.Text is null;
    }
}
