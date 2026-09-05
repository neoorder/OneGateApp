using NeoOrder.OneGate.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeoOrder.OneGate.Data;

public class Banner : IComparable<Banner>, ICachedEntity
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }
    [Url]
    public required string ImageUrl { get; set; }
    [Url]
    public required string TargetUrl { get; set; }
    public string? AltText { get; set; }

    int IComparable<Banner>.CompareTo(Banner? other)
    {
        if (other is null) return 1;
        return -Id.CompareTo(other.Id);
    }
}
