using Plugin.Maui.ScreenSecurity;

namespace NeoOrder.OneGate.Services;

static class ScreenSecurityCoordinator
{
    static readonly object syncRoot = new();
    static int activeScopes;
    static int generation;

    public static void Enter(IScreenSecurity screenSecurity)
    {
        bool shouldActivate = false;
        lock (syncRoot)
        {
            activeScopes++;
            generation++;
            shouldActivate = activeScopes == 1;
        }
        if (!shouldActivate || screenSecurity.IsProtectionEnabled)
            return;
        screenSecurity.ActivateScreenSecurityProtection();
    }

    public static void Exit(IScreenSecurity screenSecurity)
    {
        int exitGeneration;
        lock (syncRoot)
        {
            if (activeScopes > 0)
                activeScopes--;
            generation++;
            exitGeneration = generation;
            if (activeScopes > 0)
                return;
        }
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Yield();
            lock (syncRoot)
            {
                if (activeScopes > 0 || exitGeneration != generation)
                    return;
            }
            if (screenSecurity.IsProtectionEnabled)
                screenSecurity.DeactivateScreenSecurityProtection();
        });
    }
}
