namespace NeoOrder.OneGate.Data;

[Flags]
public enum DAppPlatforms : byte
{
    Android = 1,
    iOS = 2,
    MacCatalyst = 4,
    Windows = 8,
    All = Android | iOS | MacCatalyst | Windows
}
