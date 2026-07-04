using GraphicsFont = Microsoft.Maui.Graphics.Font;

namespace NeoOrder.OneGate.Services;

sealed partial class ProgressWindowOverlay : IDisposable
{
    readonly Window window;
    readonly WindowOverlay overlay;
    readonly ProgressOverlayElement element;
    readonly IDispatcherTimer timer;
    bool disposed;
    int frame;

    public ProgressWindowOverlay(Window window, string title, string message)
    {
        this.window = window;
        element = new(title, message, GetThemeColors());
        overlay = new WindowOverlay(window)
        {
            DisableUITouchEventPassthrough = true,
            EnableDrawableTouchHandling = true,
            IsVisible = true
        };
        overlay.AddWindowElement(element);
        ((IWindow)window).AddOverlay(overlay);

        timer = window.Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(80);
        timer.Tick += Timer_Tick;
        timer.Start();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop();
        overlay.RemoveWindowElement(element);
        ((IWindow)window).RemoveOverlay(overlay);
    }

    void Timer_Tick(object? sender, EventArgs e)
    {
        frame++;
        element.Frame = frame;
        overlay.Invalidate();
    }

    static ThemeColors GetThemeColors()
    {
        bool dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        return dark
            ? new(
                Color.FromArgb("#0B0F14"),
                Color.FromArgb("#F4F7FB"),
                Color.FromArgb("#A2AAB8"))
            : new(
                Colors.White,
                Color.FromArgb("#171A22"),
                Color.FromArgb("#737B8C"));
    }

    sealed class ProgressOverlayElement(string title, string message, ThemeColors colors) : IWindowOverlayElement
    {
        const float CardWidth = 260;
        const float CardHeight = 124;
        const float Radius = 10;
        const float Padding = 18;
        const float SpinnerSize = 24;

        public int Frame { get; set; }

        public bool Contains(Point point) => true;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0) return;

            float width = Math.Min(CardWidth, Math.Max(0, dirtyRect.Width - 48));
            float height = CardHeight;
            float x = dirtyRect.X + (dirtyRect.Width - width) / 2;
            float y = dirtyRect.Y + (dirtyRect.Height - height) / 2;

            canvas.SaveState();
            canvas.FillColor = Colors.Black.WithAlpha(0.38f);
            canvas.FillRectangle(dirtyRect);
            canvas.RestoreState();

            canvas.SaveState();
            canvas.SetShadow(new SizeF(0, 6), 20, Colors.Black.WithAlpha(0.22f));
            canvas.FillColor = colors.Background;
            canvas.FillRoundedRectangle(x, y, width, height, Radius);
            canvas.RestoreState();

            canvas.FontColor = colors.Primary;
            canvas.Font = GraphicsFont.DefaultBold;
            canvas.FontSize = 14;
            canvas.DrawString(title, x + Padding, y + 14, width - Padding * 2, 24, HorizontalAlignment.Left, VerticalAlignment.Center, TextFlow.ClipBounds, 0);

            float contentY = y + 62;
            float spinnerX = x + Padding;
            float spinnerY = contentY + 1;
            float angle = (Frame * 28) % 360;

            canvas.StrokeColor = colors.Secondary.WithAlpha(0.28f);
            canvas.StrokeSize = 3;
            canvas.DrawArc(spinnerX, spinnerY, SpinnerSize, SpinnerSize, 0, 360, false, false);

            canvas.StrokeColor = colors.Secondary;
            canvas.StrokeSize = 3;
            canvas.DrawArc(spinnerX, spinnerY, SpinnerSize, SpinnerSize, angle, angle + 270, false, false);

            canvas.Font = GraphicsFont.Default;
            canvas.FontColor = colors.Primary;
            canvas.FontSize = 14;
            canvas.DrawString(message, spinnerX + SpinnerSize + 14, contentY - 4, width - Padding * 2 - SpinnerSize - 14, 42, HorizontalAlignment.Left, VerticalAlignment.Center, TextFlow.ClipBounds, 0);
        }
    }

    readonly record struct ThemeColors(Color Background, Color Primary, Color Secondary);
}
