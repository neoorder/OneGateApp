using System.ComponentModel.DataAnnotations;

namespace NeoOrder.OneGate.Data;

public class ExcludedNewsCategory
{
    [Key]
    public required string Category { get; set; }
}
