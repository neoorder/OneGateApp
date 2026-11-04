using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeoOrder.OneGate.Data;

public class DAppRecentVisit
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int DAppId { get; set; }
    public bool IsGame { get; set; }
    public DateTime LastOpenedAt { get; set; }
}
