// Only MAUI directory access and unused diagnostic UI types are stubbed.
// RpcClient, its serializers, transaction models, and Neo wallets are real sources.
namespace NeoOrder.OneGate.Services
{
    static class FileSystem
    {
        public static string AppDataDirectory => Path.GetTempPath();
        public static string CacheDirectory => Path.GetTempPath();
    }
}

namespace NeoOrder.OneGate.Models.Diagnostics
{
    public class Diagnostic { }
}
