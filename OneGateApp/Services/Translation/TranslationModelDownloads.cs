namespace NeoOrder.OneGate.Services.Translation;

internal sealed class TranslationModelDownloads
{
    internal sealed record Download(TranslationDownloadPolicy Policy, Task Completion, bool MayReuseWifi = false);

    readonly Dictionary<string, Download> pending = new(StringComparer.Ordinal);

    public bool Contains(string language) => FindPending(language) is not null;

    public Download? FindPending(string language)
    {
        lock (pending)
            return pending.TryGetValue(language, out var download) && !download.Completion.IsCompleted ? download : null;
    }

    public Download? GetOrStart(string language, Func<Download?> start)
    {
        lock (pending)
        {
            if (pending.TryGetValue(language, out var download) && !download.Completion.IsCompleted) return download;
            download = start();
            if (download is null) return null;
            pending[language] = download;
            _ = ForgetWhenCompleteAsync(language, download);
            return download;
        }
    }

    async Task ForgetWhenCompleteAsync(string language, Download download)
    {
        // Observe failures even when the foreground caller has stopped waiting.
        await download.Completion.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        lock (pending)
            if (pending.TryGetValue(language, out var current) && ReferenceEquals(current, download))
                pending.Remove(language);
    }
}
