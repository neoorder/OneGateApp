using System.ComponentModel.DataAnnotations;

namespace NeoOrder.OneGate.Data;

public class HiddenToken
{
    [Key]
    public required string Hash { get; set; }
}
