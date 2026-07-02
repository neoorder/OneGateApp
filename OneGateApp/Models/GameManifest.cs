using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeoOrder.OneGate.Models;

public enum GamePreferredOrientation
{
    Any,
    Portrait,
    Landscape
}

public enum GameInputMethod
{
    Touch,
    Pointer,
    Keyboard,
    Gamepad
}

public sealed class GameAssetBudget
{
    public long? InitialBytes { get; init; }
    public long? TotalFirstLoadBytes { get; init; }
    public long? JsWasmBytes { get; init; }
    public long? TextureBytes { get; init; }
    public long? AudioBytes { get; init; }
}

public sealed class GameManifest
{
    public const int CurrentSchemaVersion = 1;

    public static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string? GameType { get; init; }
    public GamePreferredOrientation PreferredOrientation { get; init; }
    public bool FullscreenPreferred { get; init; }
    public GameAssetBudget? AssetBudget { get; init; }
    public IReadOnlyList<string> RequiredOneGateApis { get; init; } = [];
    public IReadOnlyList<GameInputMethod> SupportedInputMethods { get; init; } = [];
    public string? Version { get; init; }
    public string? PackageHash { get; init; }

    public bool IsSupportedSchemaVersion => SchemaVersion == CurrentSchemaVersion;
}
