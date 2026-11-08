#if IOS || MACCATALYST
using OneGate.Translation.Apple;

namespace NeoOrder.OneGate.Services.Translation;

public sealed class ChatTranslationService : IChatTranslationService
{
#if IOS
    public bool IsAvailable => OperatingSystem.IsIOSVersionAtLeast(18);
#else
    public bool IsAvailable => OperatingSystem.IsMacCatalystVersionAtLeast(26);
#endif
    public bool CanManageModels => false;

    public async Task<IReadOnlyList<string>> GetLanguagesAsync(CancellationToken cancellationToken)
    {
        if (!IsAvailable) return [];
        cancellationToken.ThrowIfCancellationRequested();
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            using var bridge = new TranslationBridge();
            var completion = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            bridge.GetLanguages((languages, status) =>
            {
                if (status == 0) completion.TrySetResult(languages);
                else completion.TrySetException(new ChatTranslationException(TranslationFailure.Unavailable));
            });
            var result = await completion.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        });
    }

    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        if (!IsAvailable) throw new ChatTranslationException(TranslationFailure.Unavailable);
        if (!TranslationLanguages.HasText(request.Text)) throw new ChatTranslationException(TranslationFailure.NoText);
        cancellationToken.ThrowIfCancellationRequested();
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var presenter = Platform.GetCurrentUIViewController() ?? throw new ChatTranslationException(TranslationFailure.Unavailable);
            using var bridge = new TranslationBridge();
            var completion = new TaskCompletionSource<TranslationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            bridge.Translate(request.Text, request.SourceLanguage!, request.TargetLanguage, presenter, (text, source, target, status) =>
            {
                if (status == 8) completion.TrySetCanceled();
                else if (status is 0 or 7 && !string.IsNullOrWhiteSpace(text) && source is not null && target is not null)
                    completion.TrySetResult(new(text, source, target, status == 7));
                else completion.TrySetException(new ChatTranslationException(status switch
                {
                    1 => TranslationFailure.Unavailable,
                    2 => TranslationFailure.UnsupportedLanguage,
                    3 => TranslationFailure.SourceLanguageRequired,
                    4 => TranslationFailure.DownloadFailed,
                    6 => TranslationFailure.NoText,
                    9 => TranslationFailure.UnsupportedSourceLanguage,
                    10 => TranslationFailure.UnsupportedTargetLanguage,
                    11 => TranslationFailure.UnsupportedLanguagePair,
                    _ => TranslationFailure.Failed
                }, (status, source, target) switch
                {
                    (9, { } from, _) => [from],
                    (11, { } from, { } to) => [from, to],
                    _ => null
                }));
            });
            using var registration = cancellationToken.Register(() => MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!completion.Task.IsCompleted) bridge.Cancel();
            }));
            var result = await completion.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        });
    }

    public Task<IReadOnlyList<string>> GetDownloadedModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task DeleteModelAsync(string language, CancellationToken cancellationToken) => Task.FromException(new ChatTranslationException(TranslationFailure.Unavailable));
}
#endif
