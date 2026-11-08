namespace NeoOrder.OneGate.Services.Translation;

public enum TranslationFailure
{
    Unavailable, UnsupportedLanguage, SourceLanguageRequired, ModelsRequired, DownloadFailed, Failed, NoText, DownloadWifiRequired,
    DownloadWifiMayBeRequired, UnsupportedSourceLanguage, UnsupportedTargetLanguage, UnsupportedLanguagePair
}

public enum TranslationDownloadPolicy { ExistingOnly, Wifi, AnyNetwork }

public sealed record TranslationRequest(string Text, string TargetLanguage, string? SourceLanguage = null);

public sealed record TranslationResult(string Text, string SourceLanguage, string TargetLanguage, bool SameLanguage = false);

public sealed class ChatTranslationException(TranslationFailure failure, IReadOnlyList<string>? languages = null)
    : Exception(failure.ToString())
{
    public TranslationFailure Failure { get; } = failure;
    public IReadOnlyList<string> Languages { get; } = languages ?? [];
}

public interface IChatTranslationService
{
    bool IsAvailable { get; }
    bool CanManageModels { get; }
    event EventHandler? ModelDownloaded { add { } remove { } }
    Task<IReadOnlyList<string>> GetLanguagesAsync(CancellationToken cancellationToken);
    Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken);
    bool HasPendingModelDownload(string language) => false;
    Task DownloadModelsAsync(IReadOnlyList<string> languages, TranslationDownloadPolicy policy, CancellationToken cancellationToken)
        => Task.FromException(new ChatTranslationException(TranslationFailure.Unavailable));
    Task<IReadOnlyList<string>> GetDownloadedModelsAsync(CancellationToken cancellationToken);
    Task DeleteModelAsync(string language, CancellationToken cancellationToken);
}
