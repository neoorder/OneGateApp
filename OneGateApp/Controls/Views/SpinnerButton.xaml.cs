namespace NeoOrder.OneGate.Controls.Views;

public partial class SpinnerButton : ContentView
{
    private partial class BusyStateSwitch : IDisposable
    {
        private readonly Page page;
        private readonly SpinnerButton spinner;

        public BusyStateSwitch(Page page, SpinnerButton spinner)
        {
            this.page = page;
            this.spinner = spinner;
            spinner.IsBusy = true;
            page.IsEnabled = false;
        }

        void IDisposable.Dispose()
        {
            page.IsEnabled = true;
            spinner.IsBusy = false;
        }
    }

    public event EventHandler? Clicked;

    public static readonly BindableProperty TextProperty = BindableProperty.Create(nameof(Text), typeof(string), typeof(SpinnerButton));
    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(SpinnerButton));
    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(nameof(FontSize), typeof(double), typeof(SpinnerButton));
    public static readonly BindableProperty FontAttributesProperty = BindableProperty.Create(nameof(FontAttributes), typeof(FontAttributes), typeof(SpinnerButton));
    public static readonly BindableProperty ButtonColorProperty = BindableProperty.Create(nameof(ButtonColor), typeof(Color), typeof(SpinnerButton));
    public static readonly BindableProperty BorderWidthProperty = BindableProperty.Create(nameof(BorderWidth), typeof(double), typeof(SpinnerButton), 0.0);
    public static readonly BindableProperty BorderColorProperty = BindableProperty.Create(nameof(BorderColor), typeof(Color), typeof(SpinnerButton));
    public static readonly BindableProperty IsBusyProperty = BindableProperty.Create(nameof(IsBusy), typeof(bool), typeof(SpinnerButton));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public Color TextColor { get => (Color)GetValue(TextColorProperty); set => SetValue(TextColorProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontAttributes FontAttributes { get => (FontAttributes)GetValue(FontAttributesProperty); set => SetValue(FontAttributesProperty, value); }
    public Color ButtonColor { get => (Color)GetValue(ButtonColorProperty); set => SetValue(ButtonColorProperty, value); }
    public double BorderWidth { get => (double)GetValue(BorderWidthProperty); set => SetValue(BorderWidthProperty, value); }
    public Color BorderColor { get => (Color)GetValue(BorderColorProperty); set => SetValue(BorderColorProperty, value); }
    public bool IsBusy { get => (bool)GetValue(IsBusyProperty); private set => SetValue(IsBusyProperty, value); }

    public SpinnerButton()
    {
        InitializeComponent();
    }

    public IDisposable EnterBusyState()
    {
        return new BusyStateSwitch(this.FindAncestor<Page>(), this);
    }

    private void Button_Clicked(object sender, EventArgs e)
    {
        if (IsBusy) return;
        Clicked?.Invoke(this, EventArgs.Empty);
    }
}
