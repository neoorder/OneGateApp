using NeoOrder.OneGate.Properties;

namespace NeoOrder.OneGate.Services.Translation;

static class TranslationText
{
    public static string Action => DeviceInfo.Platform == DevicePlatform.Android ? Strings.TranslateWithGoogle : Strings.TranslateMessage;
    public static bool ShowGoogleAttribution => DeviceInfo.Platform == DevicePlatform.Android;
    public static string Error(Exception exception) => exception is ChatTranslationException error ? error.Failure switch
    {
        TranslationFailure.UnsupportedTargetLanguage => Strings.TranslationLanguageUnsupported,
        TranslationFailure.UnsupportedSourceLanguage when error.Languages is [var source] =>
            string.Format(Strings.TranslationSourceLanguageUnsupported, TranslationLanguages.DisplayName(source)),
        TranslationFailure.UnsupportedLanguagePair when error.Languages is [var source, var target] =>
            string.Format(Strings.TranslationLanguagePairUnsupported, TranslationLanguages.DisplayName(source), TranslationLanguages.DisplayName(target)),
        TranslationFailure.UnsupportedLanguage or TranslationFailure.UnsupportedSourceLanguage or TranslationFailure.UnsupportedLanguagePair =>
            Strings.TranslationLanguagesUnsupported,
        TranslationFailure.SourceLanguageRequired => Strings.TranslationChooseSource,
        TranslationFailure.DownloadWifiRequired => Strings.TranslationDownloadWifiRequired,
        TranslationFailure.DownloadWifiMayBeRequired => Strings.TranslationDownloadWifiMayBeRequired,
        TranslationFailure.DownloadFailed or TranslationFailure.ModelsRequired => Strings.TranslationDownloadFailed,
        TranslationFailure.NoText => Strings.TranslationNoText,
        _ => Strings.TranslationFailed
    } : Strings.TranslationFailed;
}
