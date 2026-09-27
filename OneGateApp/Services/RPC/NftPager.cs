using NeoOrder.OneGate.Models;
using System.Runtime.CompilerServices;

namespace NeoOrder.OneGate.Services.RPC;

internal readonly record struct NftIteratorSession(Guid Id, Guid[] Iterators);

internal static class NftPager
{
    public static async IAsyncEnumerable<NFT[]> ReadAsync(
        Func<CancellationToken, Task<NftIteratorSession>> open,
        Func<Guid, Guid, int, CancellationToken, Task<byte[][]>> traverse,
        Func<int, byte[][], CancellationToken, Task<NFT[]>> metadata,
        Func<Guid, Task> close,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        NftIteratorSession session = await open(cancellationToken);
        var page = new List<NFT>(100);
        try
        {
            for (int collection = 0; collection < session.Iterators.Length; collection++)
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = Math.Min(25, 100 - page.Count);
                    byte[][] ids = await traverse(session.Id, session.Iterators[collection], count, cancellationToken);
                    if (ids.Length == 0) break;
                    if (ids.Length > count) throw new InvalidDataException("NFT iterator returned more items than requested.");
                    NFT[] items = await metadata(collection, ids, cancellationToken);
                    if (items.Length != ids.Length) throw new InvalidDataException("NFT metadata response is incomplete.");
                    page.AddRange(items);
                    if (page.Count == 100)
                    {
                        yield return page.ToArray();
                        page.Clear();
                    }
                    // Nodes may enforce a smaller traversal limit. Only an empty reply
                    // proves exhaustion, not a reply shorter than the requested batch.
                }
            }
        }
        finally
        {
            await close(session.Id);
        }
        if (page.Count > 0) yield return page.ToArray();
    }
}
