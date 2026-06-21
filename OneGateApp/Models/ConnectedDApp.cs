using System.Globalization;
using NeoOrder.OneGate.Properties;

namespace NeoOrder.OneGate.Models;

public class ConnectedDApp
{
    public required string Domain { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset ConnectedAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Domain : Name;
    public string ConnectedAtText => string.Format(Strings.ConnectedAtFormat, ConnectedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
    public string LastUsedAtText => string.Format(Strings.LastUsedAtFormat, LastUsedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
}
