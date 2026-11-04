using System.ComponentModel.DataAnnotations;

namespace NeoOrder.OneGate.Data;

public class CacheState
{
    [Key]
    public required string Name { get; set; }
    public DateTime LastUpdatedAt { get; set; }
}
