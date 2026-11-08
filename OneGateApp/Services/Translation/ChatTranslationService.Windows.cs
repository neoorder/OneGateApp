#if WINDOWS
namespace NeoOrder.OneGate.Services.Translation;

public sealed class ChatTranslationService : IChatTranslationService
{
    public bool IsAvailable => false;
    public bool CanManageModels => false;
    public Task<IReadOnlyList<string>> GetLanguagesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<IReadOnlyList<string>> GetDownloadedModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task DeleteModelAsync(string language, CancellationToken cancellationToken) => Task.FromException(new ChatTranslationException(TranslationFailure.Unavailable));
    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken) => Task.FromException<TranslationResult>(new ChatTranslationException(TranslationFailure.Unavailable));
}
#endif
