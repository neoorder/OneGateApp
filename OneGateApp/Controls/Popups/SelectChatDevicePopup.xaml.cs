using Meshline.Models.Client;
using NeoOrder.OneGate.Properties;

namespace NeoOrder.OneGate.Controls.Popups;

public partial class SelectChatDevicePopup : MyPopup<string?>
{
    CancellationTokenRegistration cancellationRegistration;
    bool closing;

    public string Description { get; }
    public string? ErrorMessage { get; }
    public ChatDeviceChoice[] Devices { get; }

    public SelectChatDevicePopup(DeviceSelectionInfo selection, CancellationToken cancellationToken, string? error = null)
    {
        Description = string.Format(Strings.ChatDeviceLimitHint, selection.DeviceLimit);
        ErrorMessage = error;
        Devices = selection.Devices.Select(device => new ChatDeviceChoice(device)).ToArray();
        InitializeComponent();
        BindingContext = this;
        // Register once the popup can be closed. An already-canceled operation closes it on opening.
        Opened += (_, _) => cancellationRegistration = cancellationToken.Register(() =>
            MainThread.BeginInvokeOnMainThread(() => OnCancel(this, EventArgs.Empty)));
        Closed += (_, _) =>
        {
            closing = true;
            cancellationRegistration.Dispose();
        };
    }

    void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        replaceButton.IsEnabled = e.CurrentSelection.FirstOrDefault() is ChatDeviceChoice;

    async void OnReplace(object sender, EventArgs e)
    {
        if (deviceList.SelectedItem is ChatDeviceChoice selected)
            await CompleteAsync(selected.DeviceId);
    }

    async void OnCancel(object sender, EventArgs e) => await CompleteAsync(null);

    async Task CompleteAsync(string? deviceId)
    {
        if (closing) return;
        closing = true;
        await CloseAsync(deviceId);
    }
}

public sealed class ChatDeviceChoice(AccountDeviceInfo device)
{
    public string DeviceId => device.DeviceId;
    public string ValidUntil => string.Format(Strings.ChatDeviceValidUntil,
        DateTimeOffset.FromUnixTimeSeconds(device.Certificate.ExpiresAt).ToLocalTime().ToString("g"));
    public bool IsExpired => device.Certificate.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
