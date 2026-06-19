using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;

namespace NeoOrder.OneGate.Controls.Views;

public partial class TabBar : ContentView
{
    public event EventHandler? SelectedTabChanged;

    public static readonly BindableProperty TabsProperty = BindableProperty.Create(nameof(Tabs), typeof(IReadOnlyList<string>), typeof(TabBar), propertyChanged: OnTabsChanged);
    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(nameof(FontSize), typeof(double), typeof(TabBar), defaultValue: 14.0);
    public static readonly BindableProperty TabColorProperty = BindableProperty.Create(nameof(TabColor), typeof(Color), typeof(TabBar));
    public static readonly BindableProperty SelectedTabColorProperty = BindableProperty.Create(nameof(SelectedTabColor), typeof(Color), typeof(TabBar));
    public static readonly BindableProperty SelectedTabBackgroundColorProperty = BindableProperty.Create(nameof(SelectedTabBackgroundColor), typeof(Color), typeof(TabBar));
    public static readonly BindableProperty SpacingProperty = BindableProperty.Create(nameof(Spacing), typeof(double), typeof(TabBar), defaultValue: 10.0);

    public IReadOnlyList<string>? Tabs
    {
        get => (IReadOnlyList<string>?)GetValue(TabsProperty);
        set => SetValue(TabsProperty, value);
    }

    public string? SelectedTab
    {
        get;
        set
        {
            if (field == value) return;
            OnPropertyChanging();
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedIndex));
            SelectedTabChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public Color TabColor
    {
        get => (Color)GetValue(TabColorProperty);
        set => SetValue(TabColorProperty, value);
    }

    public Color SelectedTabColor
    {
        get => (Color)GetValue(SelectedTabColorProperty);
        set => SetValue(SelectedTabColorProperty, value);
    }

    public Color SelectedTabBackgroundColor
    {
        get => (Color)GetValue(SelectedTabBackgroundColorProperty);
        set => SetValue(SelectedTabBackgroundColorProperty, value);
    }

    public int SelectedIndex
    {
        get
        {
            if (Tabs is null || SelectedTab is null) return -1;
            for (int i = 0; i < Tabs.Count; i++)
                if (Tabs[i] == SelectedTab)
                    return i;
            return -1;
        }
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public TabBar()
    {
        this.SetAppThemeColor(TabColorProperty, (AppThemeColor)Application.Current!.Resources["Secondary"]);
        this.SetAppThemeColor(SelectedTabColorProperty, (AppThemeColor)Application.Current.Resources["Primary"]);
        this.SetAppThemeColor(SelectedTabBackgroundColorProperty, (AppThemeColor)Application.Current.Resources["SelectedTabBackground"]);
        InitializeComponent();
    }

    static void OnTabsChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (oldValue == newValue) return;
        TabBar tabBar = (TabBar)bindable;
        IReadOnlyList<string>? value = (IReadOnlyList<string>?)newValue;
        if (tabBar.SelectedTab == null)
        {
            if (value?.Count > 0)
                tabBar.SelectedTab = value[0];
        }
        else
        {
            if (value is null || value.Count == 0 || !value.Contains(tabBar.SelectedTab))
                tabBar.SelectedTab = null;
        }
    }

    void Tab_Tapped(object sender, TappedEventArgs e)
    {
        if (sender is Border { Content: Label label })
            SelectedTab = label.Text;
    }
}
