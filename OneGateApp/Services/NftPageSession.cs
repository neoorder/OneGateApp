using NeoOrder.OneGate.Models;

namespace NeoOrder.OneGate.Services;

// Owns one UI pagination session. Closing cancels a pending read before disposing
// the async iterator, which must never be disposed during MoveNextAsync.
internal sealed class NftPageSession : IAsyncDisposable
{
    readonly CancellationTokenSource cancellation = new();
    readonly SemaphoreSlim gate = new(1);
    readonly IAsyncEnumerator<NFT[]> pages;
    Task? closeTask;

    public NftPageSession(Func<CancellationToken, IAsyncEnumerable<NFT[]>> createPages)
    {
        pages = createPages(cancellation.Token).GetAsyncEnumerator(cancellation.Token);
    }

    public async Task<NFT[]?> ReadNextAsync()
    {
        await gate.WaitAsync(cancellation.Token);
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            return await pages.MoveNextAsync() ? pages.Current : null;
        }
        finally
        {
            gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate) return new(closeTask ??= CloseAsync());
    }

    async Task CloseAsync()
    {
        cancellation.Cancel();
        await gate.WaitAsync();
        try { await pages.DisposeAsync(); }
        finally
        {
            gate.Release();
            cancellation.Dispose();
        }
    }
}
