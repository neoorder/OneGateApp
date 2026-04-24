#if !(ANDROID || WINDOWS)

using NeoOrder.OneGate.Data;

namespace NeoOrder.OneGate.Services;

class HomeShortcutService : IHomeShortcutService
{
    public bool IsSupported => false;

    public Task<bool> AddShortcutAsync(DApp dapp)
    {
        throw new PlatformNotSupportedException();
    }
}

#endif
