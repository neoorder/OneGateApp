namespace NeoOrder.OneGate.Services;

static class SensitiveContentProtection
{
    public static void HideFromPlatformAutomation(params VisualElement?[] elements)
    {
#if ANDROID
        foreach (VisualElement? element in elements)
            AndroidSensitiveContentProtection.Hide(element);
#endif
    }
}
