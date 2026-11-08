namespace NeoOrder.OneGate.Services.Translation;

/// <summary>Serializes native work and prompts across pages.</summary>
public sealed class TranslationScheduler
{
    readonly SemaphoreSlim gate = new(1);
    internal TranslationModelDownloads AuthorizedModelDownloads { get; } = new();

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Cancellation must not release the gate while native work is still running.
            var result = await action(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally { gate.Release(); }
    }
}

public sealed record TranslationKey(string Account, string Conversation, string Message, string Text, string TargetLanguage);

/// <summary>Owned by a visible conversation. No translated content is persisted.</summary>
public sealed class TranslationSession : IDisposable
{
    readonly object sync = new();
    readonly Dictionary<TranslationKey, Task<TranslationResult>> requests = [];
    CancellationTokenSource lifetime = new();
    bool disposed;

    public Task<TranslationResult> TranslateAsync(TranslationKey key, Func<CancellationToken, Task<TranslationResult>> translate)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (requests.TryGetValue(key, out var existing)) return existing;
            var completion = new TaskCompletionSource<TranslationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            requests.Add(key, completion.Task);
            _ = RunAsync(key, translate, lifetime.Token, completion);
            return completion.Task;
        }
    }

    async Task RunAsync(TranslationKey key, Func<CancellationToken, Task<TranslationResult>> translate,
        CancellationToken token, TaskCompletionSource<TranslationResult> completion)
    {
        // A conversation can leave before its native work finishes.
        using var registration = token.Register(() => completion.TrySetCanceled(token));
        try
        {
            var result = await translate(token);
            token.ThrowIfCancellationRequested();
            completion.TrySetResult(result);
        }
        catch (OperationCanceledException) { RemoveFailed(); completion.TrySetCanceled(); }
        catch (Exception ex) { RemoveFailed(); completion.TrySetException(ex); }

        void RemoveFailed()
        {
            lock (sync)
                if (requests.TryGetValue(key, out var current) && ReferenceEquals(current, completion.Task)) requests.Remove(key);
        }
    }

    public void Reset()
    {
        CancellationTokenSource previous;
        lock (sync)
        {
            if (disposed) return;
            previous = lifetime;
            lifetime = new();
            requests.Clear();
        }
        previous.Cancel();
        previous.Dispose();
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            requests.Clear();
        }
        lifetime.Cancel();
        lifetime.Dispose();
    }
}
