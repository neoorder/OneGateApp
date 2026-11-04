using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeoOrder.OneGate.Data;

public class AppUpdateState
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }
    public required string AppVersion { get; set; }
    public bool UpdateAvailable { get; set; }
    public DateTime? NextCheckAt { get; set; }
}
