using Neo;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Services.RPC;
using NeoOrder.OneGate.Services;
using System.Runtime.CompilerServices;
using Xunit;

public class NftPagerTests
{
    sealed class Fixture
    {
        public readonly Guid Session = Guid.NewGuid();
        public readonly Guid[] Iterators = [Guid.NewGuid(), Guid.NewGuid()];
        public readonly int[] Remaining = [125, 12];
        public int Closed;
        public bool FailMetadata;
        public bool OversizedBatch;
        public List<int> Requested = [];
        public List<int> MetadataBatchSizes = [];
        public int MetadataCount;

        public IAsyncEnumerable<NFT[]> Pages(CancellationToken token = default) => NftPager.ReadAsync(
            _ => Task.FromResult(new NftIteratorSession(Session, Iterators)),
            (session, iterator, count, cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested();
                Assert.Equal(Session, session);
                Requested.Add(count);
                int index = Array.IndexOf(Iterators, iterator);
                int available = OversizedBatch ? count + 1 : Math.Min(Math.Min(count, 7), Remaining[index]);
                var ids = Enumerable.Range(0, available).Select(_ => BitConverter.GetBytes(--Remaining[index])).ToArray();
                return Task.FromResult(ids);
            },
            (collection, ids, _) =>
            {
                if (FailMetadata) throw new HttpRequestException("metadata offline");
                MetadataBatchSizes.Add(ids.Length);
                MetadataCount += ids.Length;
                return Task.FromResult(ids.Select(id => new NFT { CollectionId = UInt160.Zero, TokenId = id, Name = $"collection{collection}-{BitConverter.ToInt32(id)}" }).ToArray());
            },
            session => { Assert.Equal(Session, session); Closed++; return Task.CompletedTask; }, token);
    }

    [Fact]
    public async Task AllCollectionsRemainReachableWithSmallServerBatchLimit()
    {
        var fixture = new Fixture();
        var pages = new List<NFT[]>();
        await foreach (var page in fixture.Pages()) pages.Add(page);
        Assert.Equal(new[] { 100, 37 }, pages.Select(p => p.Length));
        Assert.Equal(137, pages.SelectMany(p => p).Select(p => p.Name).Distinct().Count());
        Assert.Contains(pages.SelectMany(p => p), p => p.Name.StartsWith("collection1-"));
        Assert.All(fixture.Requested, n => Assert.InRange(n, 1, 25));
        Assert.All(fixture.MetadataBatchSizes, n => Assert.InRange(n, 1, 25));
        Assert.Equal(1, fixture.Closed);
    }

    [Fact]
    public async Task DisposingAfterFirstPageStopsRequestsAndClosesSession()
    {
        var fixture = new Fixture();
        await using (var pages = fixture.Pages().GetAsyncEnumerator())
        {
            Assert.True(await pages.MoveNextAsync());
            Assert.Equal(100, pages.Current.Length);
            Assert.Equal(100, fixture.MetadataCount);
        }
        Assert.Equal(1, fixture.Closed);
    }

    [Fact]
    public async Task CancellationClosesSession()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        await using var pages = fixture.Pages(cancellation.Token).GetAsyncEnumerator();
        Assert.True(await pages.MoveNextAsync());
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pages.MoveNextAsync());
        Assert.Equal(1, fixture.Closed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedMetadataOrOversizedReplyClosesSession(bool oversized)
    {
        var fixture = new Fixture { FailMetadata = !oversized, OversizedBatch = oversized };
        await using var pages = fixture.Pages().GetAsyncEnumerator();
        await Assert.ThrowsAnyAsync<Exception>(async () => await pages.MoveNextAsync());
        Assert.Equal(1, fixture.Closed);
    }

    [Fact]
    public async Task ClosingUiSessionWaitsForCancelledReadBeforeDisposing()
    {
        int closed = 0;
        var started = new TaskCompletionSource();
        async IAsyncEnumerable<NFT[]> Blocked([EnumeratorCancellation] CancellationToken token)
        {
            try
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                yield return [];
            }
            finally { closed++; }
        }
        var session = new NftPageSession(Blocked);
        Task<NFT[]?> read = session.ReadNextAsync();
        await started.Task;
        await session.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        await session.DisposeAsync();
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task NodeTimeoutPropagatesAsFailureAndClosesSession()
    {
        int closed = 0;
        var sessionId = Guid.NewGuid();
        var pages = NftPager.ReadAsync(
            _ => Task.FromResult(new NftIteratorSession(sessionId, [Guid.NewGuid()])),
            (_, _, _, _) => Task.FromException<byte[][]>(new TaskCanceledException("HTTP timeout")),
            (_, _, _) => Task.FromResult(Array.Empty<NFT>()),
            _ => { closed++; return Task.CompletedTask; });
        await using var session = new NftPageSession(_ => pages);
        await Assert.ThrowsAsync<TaskCanceledException>(() => session.ReadNextAsync());
        Assert.Equal(1, closed);
    }
}
