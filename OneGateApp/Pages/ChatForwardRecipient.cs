using System.ComponentModel;

namespace NeoOrder.OneGate.Pages;

public sealed class ChatForwardRecipient(string address, string? displayName, string? label = null) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Address { get; } = address;
    public string? Label { get; } = label;
    public string? DisplayName { get; private set; } = displayName;

    internal bool UpdateDisplayName(string? value)
    {
        if (DisplayName == value) return false;
        // Keep the selected recipient's identity stable while updating both list and preview bindings.
        DisplayName = value;
        PropertyChanged?.Invoke(this, new(nameof(DisplayName)));
        return true;
    }

    public bool MatchesSearch(string filter) => filter.Length == 0
        || DisplayName?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true
        || Address.Contains(filter, StringComparison.OrdinalIgnoreCase);
}
