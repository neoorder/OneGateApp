namespace NeoOrder.OneGate.Services.Translation;

public sealed record TranslationPrompts(
    Func<bool, IReadOnlyList<string>, CancellationToken, Task<string?>> ChooseLanguage,
    Func<IReadOnlyList<string>, CancellationToken, Task<TranslationDownloadPolicy?>> ConfirmDownload);

/// <summary>Device-independent preparation flow; only explicit user actions permit a model download.</summary>
public sealed class TranslationWorkflow(IChatTranslationService service, TranslationScheduler scheduler)
{
    public async Task<TranslationResult> RunAsync(TranslationRequest request, TranslationPrompts prompts, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!service.IsAvailable) throw new ChatTranslationException(TranslationFailure.Unavailable);
        if (!TranslationLanguages.HasText(request.Text)) throw new ChatTranslationException(TranslationFailure.NoText);
        var languages = await service.GetLanguagesAsync(token);
        token.ThrowIfCancellationRequested();
        string? target = TranslationLanguages.Match(request.TargetLanguage, languages);
        if (target is null)
        {
            target = await scheduler.RunAsync(ct => prompts.ChooseLanguage(false, languages, ct), token);
            token.ThrowIfCancellationRequested();
            if (target is null) throw new OperationCanceledException(token);
            if (!languages.Contains(target)) throw new ChatTranslationException(TranslationFailure.UnsupportedTargetLanguage);
        }
        request = request with { TargetLanguage = target };
        bool downloaded = false;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return await scheduler.RunAsync(ct => service.TranslateAsync(request, ct), token);
            }
            catch (ChatTranslationException ex) when (ex.Failure == TranslationFailure.SourceLanguageRequired && request.SourceLanguage is null)
            {
                token.ThrowIfCancellationRequested();
                string? source = await scheduler.RunAsync(ct => prompts.ChooseLanguage(true, languages, ct), token);
                token.ThrowIfCancellationRequested();
                if (source is null) throw new OperationCanceledException(token);
                if (!languages.Contains(source)) throw new ChatTranslationException(TranslationFailure.UnsupportedSourceLanguage, [source]);
                request = request with { SourceLanguage = source };
            }
            catch (ChatTranslationException ex) when (ex.Failure == TranslationFailure.ModelsRequired && !downloaded)
            {
                await EnsureModelsAsync(ex.Languages, prompts, token);
                downloaded = true;
            }
        }
    }

    async Task EnsureModelsAsync(IReadOnlyList<string> languages, TranslationPrompts prompts, CancellationToken token)
    {
        while (true)
        {
            var (started, pending, authorized) = await scheduler.RunAsync(async ct =>
            {
                var active = languages.Where(service.HasPendingModelDownload).ToArray();
                var candidates = languages.Except(active).Select(language =>
                    (Language: language, Download: scheduler.AuthorizedModelDownloads.FindPending(language))).ToArray();
                var installed = await service.GetDownloadedModelsAsync(ct);
                ct.ThrowIfCancellationRequested();
                var missing = candidates.Where(model => model.Download is null && !installed.Contains(model.Language))
                    .Select(model => model.Language).ToArray();
                Task? started = null;
                if (missing.Length > 0)
                {
                    var policy = await prompts.ConfirmDownload(missing, ct);
                    ct.ThrowIfCancellationRequested();
                    if (policy is null) throw new OperationCanceledException(ct);
                    if (policy == TranslationDownloadPolicy.ExistingOnly) throw new ChatTranslationException(TranslationFailure.DownloadFailed);
                    // Register the grant before another request can prompt, even if SDK startup awaits a model check.
                    foreach (string language in missing)
                    {
                        started = DownloadAfterAsync(started, language, policy.Value, ct);
                        var authorization = new TranslationModelDownloads.Download(policy.Value, started);
                        scheduler.AuthorizedModelDownloads.GetOrStart(language, () => authorization);
                    }
                }

                var authorized = candidates.Where(model => model.Download is not null && !installed.Contains(model.Language))
                    .Select(model => model.Download!.Completion).Distinct().ToArray();
                return (started, active.Except(installed).ToArray(), authorized);
            }, token);

            if (started is not null) await started;
            if (pending.Length > 0)
                await service.DownloadModelsAsync(pending, TranslationDownloadPolicy.ExistingOnly, token);
            try
            {
                await Task.WhenAll(authorized).WaitAsync(token);
                return;
            }
            catch when (!token.IsCancellationRequested)
            {
                // The first caller may have left before the SDK started every model in the approved batch.
            }
        }
    }

    async Task DownloadAfterAsync(Task? previous, string language, TranslationDownloadPolicy policy, CancellationToken token)
    {
        if (previous is not null) await previous;
        token.ThrowIfCancellationRequested();
        await service.DownloadModelsAsync([language], policy, token);
    }
}
