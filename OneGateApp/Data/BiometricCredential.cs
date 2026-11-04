using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeoOrder.OneGate.Data;

public class BiometricCredential
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }
    // Apple platforms store the password in Keychain and use an empty blob as the enabled marker.
    public required byte[] ProtectedData { get; set; }
}
