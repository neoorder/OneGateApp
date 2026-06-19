using Microsoft.EntityFrameworkCore;
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
    public required string Label { get; set; }

    [NotMapped]
    public string AvatarText
    {
        get
        {
            string label = Label.Trim();
            return label.Length == 0 ? "?" : label[..1].ToUpperInvariant();
        }
    }
}
