using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace NeoOrder.OneGate.Data;

public class Contact
{
    [Key]
    [StringLength(34)]
    [Unicode(false)]
    public required string Address { get; set; }
    [MaxLength(100)]
    public required string Label { get; set; }
    [MaxLength(500)]
    public string? Note { get; set; }
    public bool IsAddressBookEntry { get; set; } = true;
}
