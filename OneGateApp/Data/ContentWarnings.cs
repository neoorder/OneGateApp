namespace NeoOrder.OneGate.Data;

[Flags]
public enum ContentWarnings : byte
{
    None = 0,
    Violence = 1,
    SexualContent = 2,
    Nudity = 4,
    Dating = 8,
    Gambling = 16
}
