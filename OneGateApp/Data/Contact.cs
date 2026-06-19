using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Properties;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeoOrder.OneGate.Data;

public class Contact
{
    [Key]
    [StringLength(34)]
    [Unicode(false)]
    public required string Address { get; set; }
    [MaxLength(100)]
    public string Label { get; set; } = "";
    [MaxLength(500)]
    public string? Note { get; set; }
    public bool IsAddressBookEntry { get; set; } = true;
    public DateTimeOffset? LastUsedAt { get; set; }
    public int TransferCount { get; set; }
    [StringLength(66)]
    [Unicode(false)]
    public string? LastTransactionHash { get; set; }

    [NotMapped]
    public string DisplayName => !string.IsNullOrWhiteSpace(Label) ? Label : ShortAddress;

    [NotMapped]
    public string AvatarText
    {
        get
        {
            string name = DisplayName.Trim();
            return name.Length == 0 ? "?" : name[..1].ToUpperInvariant();
        }
    }

    [NotMapped]
    public string ShortAddress => Address.Length <= 14 ? Address : $"{Address[..6]}...{Address[^6..]}";

    [NotMapped]
    public string BadgeText => IsAddressBookEntry ? Strings.AddressBook : Strings.RecentRecipient;

    [NotMapped]
    public string DetailText
    {
        get
        {
            string? lastTransfer = LastUsedAt is null ? null : string.Format(Strings.LastTransferFormat, LastUsedAt.Value.LocalDateTime);
            if (!string.IsNullOrWhiteSpace(Note) && lastTransfer is not null) return $"{Note} · {lastTransfer}";
            if (!string.IsNullOrWhiteSpace(Note)) return Note!;
            if (lastTransfer is not null) return lastTransfer;
            return Address;
        }
    }
}

public class ContactTransfer
{
    [Key]
    public int Id { get; set; }
    [StringLength(34)]
    [Unicode(false)]
    public required string Address { get; set; }
    [StringLength(66)]
    [Unicode(false)]
    public required string TransactionHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    [MaxLength(32)]
    public string? AssetSymbol { get; set; }
    [MaxLength(100)]
    public string? Amount { get; set; }
    [MaxLength(100)]
    public string? Memo { get; set; }

    [NotMapped]
    public string DisplayTitle => string.IsNullOrWhiteSpace(AssetSymbol)
        ? Strings.Transfer
        : $"{Strings.Transfer} {AssetSymbol}";

    [NotMapped]
    public string DisplaySubtitle => string.IsNullOrWhiteSpace(Amount)
        ? CreatedAt.LocalDateTime.ToString("g")
        : $"{Amount} · {CreatedAt.LocalDateTime:g}";
}
