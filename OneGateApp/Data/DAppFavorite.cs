using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeoOrder.OneGate.Data;

public class DAppFavorite
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int DAppId { get; set; }
    public DateTime CreatedAt { get; set; }
}
