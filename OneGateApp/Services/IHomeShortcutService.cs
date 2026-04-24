using NeoOrder.OneGate.Data;

namespace NeoOrder.OneGate.Services;

public interface IHomeShortcutService
{
    public bool IsSupported { get; }

    public Task<bool> AddShortcutAsync(DApp dapp);
}
