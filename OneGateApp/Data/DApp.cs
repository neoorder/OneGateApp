using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace NeoOrder.OneGate.Data;

public class DApp : IComparable<DApp>, IShareable, IVersioned
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }
    public bool IsActive { get; set; }
    public required string Name { get; set; }
    [Url]
    public required string Url { get; set; }
    [Url]
    public string? IconUrl { get; set; }
    public string[]? Tags { get; set; }
    [MaxLength(16)]
    public string? GameType { get; set; }
    public required string[] Languages { get; set; }
    [MaxLength(32)]
    public string? Developer { get; set; }
    [Url]
    public string? Website { get; set; }
    public string[]? Previews { get; set; }
    public string? Description { get; set; }
    public int Version { get; set; }

    public bool IsGamingApp => !string.IsNullOrWhiteSpace(GameType);
    public bool IsRegularApp => !IsGamingApp;
    public string? GameTypeDisplayName => LocalizeGameType(GameType);
    public Dictionary<string, string> NameLocalizer => field ??= JsonSerializer.Deserialize<Dictionary<string, string>>(Name)!;
    public Dictionary<string, string>? DescriptionLocalizer => Description is null ? null : field ??= JsonSerializer.Deserialize<Dictionary<string, string>>(Description);
    string IShareable.Text => string.Format(Strings.ShareAppText, NameLocalizer.Localize());
    string IShareable.Uri => $"https://{SharedOptions.OneGateDomain}/app/{Id}";

    int IComparable<DApp>.CompareTo(DApp? other)
    {
        if (other is null) return 1;
        return Id.CompareTo(other.Id);
    }

    public static string LocalizeTag(string tag)
    {
        return Strings.ResourceManager.GetString(tag) ?? tag;
    }

    public static string? LocalizeGameType(string? gameType)
    {
        if (string.IsNullOrWhiteSpace(gameType)) return null;
        return Strings.ResourceManager.GetString($"GameType{gameType}") ?? gameType;
    }
}
