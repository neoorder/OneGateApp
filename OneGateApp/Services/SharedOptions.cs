using NeoOrder.OneGate.Services.RPC.Converters;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeoOrder.OneGate.Services;

static class SharedOptions
{
    public const string OneGateDomain = "onegate.space";
    public static readonly string DbPath = Path.Combine(FileSystem.AppDataDirectory, "settings.db3");
    public static readonly string WalletPath = Path.Combine(FileSystem.AppDataDirectory, "wallet.json");
    public static readonly Uri[] RpcServerUris = RpcEndpointPool.DefaultEndpoints;
    public static readonly Uri RpcServerUri = RpcServerUris[0];
    public static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerOptions.Web)
    {
        Converters =
        {
            new BigIntegerConverter(),
            new ContractParameterConverter(),
            new ContractParametersContextConverter(),
            new ContractStateConverter(),
            new ECPointConverter(),
            new JsonStringEnumConverter(),
            new LongConverter(),
            new MethodTokenConverter(),
            new NefFileConverter(),
            new SignerConverter(),
            new StackItemConverter(),
            new TransactionAttributeConverter(),
            new UInt160Converter(),
            new UInt256Converter(),
            new UlongConverter()
        },
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
}
