using System.Globalization;
using NeoOrder.OneGate.Properties;

namespace NeoOrder.OneGate.Services.Translation;

public sealed class TranslationPreferences
{
    const string TargetKey = "onegate.translation.target";
    public event EventHandler? Changed;
    public string TargetLanguage
    {
        get => Preferences.Default.Get(TargetKey, "");
        set
        {
            if (value == TargetLanguage) return;
            Preferences.Default.Set(TargetKey, value);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
    public string EffectiveTarget => TargetLanguage is { Length: > 0 } target ? target
        : TranslationLanguages.ResolveInterfaceLanguage(Strings.ResourceManager, Strings.Culture ?? CultureInfo.CurrentUICulture);
}
