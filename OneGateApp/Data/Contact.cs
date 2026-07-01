using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Properties;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;

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

    [NotMapped]
    public string DisplayName => !string.IsNullOrWhiteSpace(Label) ? Label : ShortAddress;

    [NotMapped]
    public string AvatarText
    {
        get
        {
            string name = DisplayName.Trim();
            return name.Length == 0 ? "?" : StringInfo.GetNextTextElement(name).ToUpperInvariant();
        }
    }

    [NotMapped]
    public string ShortAddress => Address.Length <= 14 ? Address : $"{Address[..6]}...{Address[^6..]}";

    [NotMapped]
    public string BadgeText => IsAddressBookEntry ? Strings.AddressBook : Strings.RecentRecipient;

    [NotMapped]
    public string DetailText => !string.IsNullOrWhiteSpace(Note) ? Note! : "";
}
