using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using Microsoft.Maui.Controls.Shapes;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services.Messaging;
using SkiaSharp;

namespace NeoOrder.OneGate.Pages;

public sealed class ChatAvatarCropPage : ContentPage
{
    readonly SKBitmap photo;
    readonly AbsoluteLayout viewport = new() { WidthRequest = 300, HeightRequest = 300, IsClippedToBounds = true, HorizontalOptions = LayoutOptions.Center };
    readonly Image image = new() { Aspect = Aspect.Fill };
    readonly Slider zoom = new() { Minimum = 1, Maximum = 5, Value = 1 };
    readonly TaskCompletionSource<byte[]?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Label error = new() { StyleClass = ["Danger"], IsVisible = false };
    double offsetX, offsetY, startX, startY, pinchZoom;
    bool closing;
    bool disposed;
    public Task<byte[]?> Result => completion.Task;

    public ChatAvatarCropPage(SKBitmap photo)
    {
        this.photo = photo;
        Unloaded += (_, _) => { if (!closing) completion.TrySetResult(null); DisposePhoto(); };
        Title = Strings.ChatAvatarCrop;
        SafeAreaEdges = SafeAreaEdges.All;
        Shell.SetTabBarIsVisible(this, false);
        using (var preview = photo.Encode(SKEncodedImageFormat.Jpeg, 90))
        {
            byte[] bytes = preview.ToArray();
            image.Source = ImageSource.FromStream(() => new MemoryStream(bytes, writable: false));
        }
        viewport.Children.Add(image);
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            if (e.StatusType == GestureStatus.Started) { startX = offsetX; startY = offsetY; }
            if (e.StatusType == GestureStatus.Running) { offsetX = startX + e.TotalX; offsetY = startY + e.TotalY; ArrangePhoto(); }
        };
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += (_, e) =>
        {
            if (e.Status == GestureStatus.Started) pinchZoom = zoom.Value;
            if (e.Status == GestureStatus.Running) { pinchZoom = Math.Clamp(pinchZoom * e.Scale, 1, 5); zoom.Value = pinchZoom; }
        };
        viewport.GestureRecognizers.Add(pan);
        viewport.GestureRecognizers.Add(pinch);
        zoom.ValueChanged += (_, _) => ArrangePhoto();
        SemanticProperties.SetDescription(zoom, Strings.ChatAvatarZoom);
        var cropShape = new RoundRectangle();
        viewport.SizeChanged += (_, _) =>
        {
            // Match the 24-point corners of the 88-point avatar in chat settings.
            cropShape.CornerRadius = new CornerRadius(Math.Max(0, Math.Min(viewport.Width, viewport.Height)) * 24 / 88);
            ArrangePhoto();
        };
        var save = new Button { Text = Strings.ChatAvatarSave };
        var cancel = new Button { Text = Strings.Cancel, StyleClass = ["Secondary"] };
        save.Clicked += async (_, _) =>
        {
            if (closing || disposed) return;
            save.IsEnabled = false;
            try
            {
                double scale = PhotoScale;
                float side = (float)(viewport.Width / scale);
                var crop = SKRect.Create((float)((photo.Width - side) / 2 - offsetX / scale),
                    (float)((photo.Height - side) / 2 - offsetY / scale), side, side);
                using var snapshot = photo.Copy();
                byte[] bytes = await Task.Run(() => ChatAvatarImages.Crop(snapshot, crop));
                await CloseAsync(bytes);
            }
            catch (Exception) { error.Text = Strings.ChatAvatarInvalidImage; error.IsVisible = true; save.IsEnabled = true; }
        };
        cancel.Clicked += async (_, _) => await CloseAsync(null);
        var buttons = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 12 };
        buttons.Add(cancel); buttons.Add(save, 1);
        // Keep the crop surface outside a ScrollView, which consumes vertical drags on Android.
        var cropHost = new Grid();
        var cropFrame = new Border
        {
            Content = viewport, StrokeShape = cropShape, StrokeThickness = 0, Padding = 0,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center
        };
        cropFrame.SetAppThemeColor(BackgroundColorProperty, (AppThemeColor)Application.Current!.Resources["BrandMuted"]);
        cropHost.Add(cropFrame);
        cropHost.SizeChanged += (_, _) =>
        {
            double side = Math.Max(1, Math.Min(300, Math.Min(cropHost.Width, cropHost.Height)));
            viewport.WidthRequest = viewport.HeightRequest = side;
        };
        var layout = new Grid
        {
            Padding = 20, RowSpacing = 20, MaximumWidthRequest = 400, HorizontalOptions = LayoutOptions.Fill,
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }
        };
        layout.Add(new Label { Text = Strings.ChatAvatarCropHint });
        layout.Add(cropHost, 0, 1);
        layout.Add(new VerticalStackLayout { Spacing = 12, Children = { zoom, error, buttons } }, 0, 2);
        Content = layout;
    }

    double PhotoScale => Math.Max(viewport.Width / photo.Width, viewport.Height / photo.Height) * zoom.Value;
    void ArrangePhoto()
    {
        if (disposed || viewport.Width <= 0 || viewport.Height <= 0) return;
        double width = photo.Width * PhotoScale, height = photo.Height * PhotoScale;
        // A fitted edge can be slightly smaller than the viewport due to floating-point rounding.
        double maxOffsetX = Math.Max(0, (width - viewport.Width) / 2);
        double maxOffsetY = Math.Max(0, (height - viewport.Height) / 2);
        offsetX = Math.Clamp(offsetX, -maxOffsetX, maxOffsetX);
        offsetY = Math.Clamp(offsetY, -maxOffsetY, maxOffsetY);
        AbsoluteLayout.SetLayoutBounds(image, new Rect((viewport.Width - width) / 2 + offsetX, (viewport.Height - height) / 2 + offsetY, width, height));
    }

    public async Task CloseAsync(byte[]? result)
    {
        if (closing) return;
        closing = true;
        try
        {
            if (Navigation.ModalStack.Contains(this)) await Navigation.PopModalAsync();
            completion.TrySetResult(result);
        }
        finally { completion.TrySetResult(null); DisposePhoto(); }
    }

    protected override bool OnBackButtonPressed() { _ = CloseAsync(null); return true; }
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!closing && !Navigation.ModalStack.Contains(this)) { completion.TrySetResult(null); DisposePhoto(); }
    }
    void DisposePhoto() { if (!disposed) { disposed = true; photo.Dispose(); } }
}
