using Neo;
using Neo.Wallets;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Pages;
using NeoOrder.OneGate.Services;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

public class WalletPageHandoverTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    sealed class UnusedWalletProvider : IWalletProvider
    {
        public Wallet? GetWallet() => null;
        public event EventHandler<Wallet?>? WalletChanged { add { } remove { } }
    }

    sealed class Inventory(int firstPageSize, bool pauseClose = false)
    {
        public int Reads;
        public int Closed;
        public readonly TaskCompletionSource CloseEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ReleaseClose = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<NFT[]> Pages([EnumeratorCancellation] CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                Reads++;
                yield return Items(firstPageSize);
                token.ThrowIfCancellationRequested();
                Reads++;
                yield return Items(37);
            }
            finally
            {
                CloseEntered.TrySetResult();
                if (pauseClose) await ReleaseClose.Task;
                Closed++;
            }
        }
    }

    static NFT[] Items(int count) => Enumerable.Range(0, count)
        .Select(i => new NFT { CollectionId = UInt160.Zero, TokenId = BitConverter.GetBytes(i), Name = $"NFT-{i}" }).ToArray();
    static void LoadMore(WalletPage page) => typeof(WalletPage).GetMethod("OnLoadMoreNFTs", Hidden)!.Invoke(page, [page, EventArgs.Empty]);
    static Task Refresh(WalletPage page) => (Task)typeof(WalletPage).GetMethod("LoadNFTsAsync", Hidden)!.Invoke(page, null)!;
    static void Leave(WalletPage page) => typeof(WalletPage).GetMethod("OnDisappearing", Hidden)!.Invoke(page, null);

    [Theory]
    [InlineData(100)]
    [InlineData(37)]
    public async Task HeldPreviousCloseCannotAdmitQueuedLoadMoreOrReadReplacementTwice(int replacementPageSize)
    {
        var previous = new Inventory(100, pauseClose: true);
        var replacement = new Inventory(replacementPageSize);
        int created = 0;
        var manager = new TokenManager(token => (++created == 1 ? previous : replacement).Pages(token));
        var page = new WalletPage(new ApplicationDbContext(), new UnusedWalletProvider(), manager);
        Assert.Equal(100, page.NFTs.Count);
        Assert.True(page.HasMoreNFTs);
        Assert.False(page.LoadingService.IsLoading);

        Task refresh = Refresh(page);
        try
        {
            await previous.CloseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(refresh.IsCompleted);
            Assert.False(page.HasMoreNFTs);
            Assert.Empty(page.NFTs);
            // Invoke the real handler even though the button should be hidden:
            // this represents a tap queued before refresh changed the binding.
            LoadMore(page);
            LoadMore(page);
            Assert.Equal(0, replacement.Reads);
            Assert.False(page.NftLoadFailed);

            previous.ReleaseClose.TrySetResult();
            await refresh.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, previous.Closed);
            Assert.Equal(1, replacement.Reads);
            Assert.Equal(replacementPageSize, page.NFTs.Count);
            Assert.Equal(replacementPageSize == 100, page.HasMoreNFTs);
            Assert.False(page.NftLoadFailed);
            Assert.False(page.IsLoadingNFTs);

            if (replacementPageSize == 100)
            {
                LoadMore(page);
                Assert.Equal(137, page.NFTs.Count);
                Assert.Equal(2, replacement.Reads);
            }
            // A queued event after the final page must not read its disposed
            // session or turn successful completion into a load failure.
            LoadMore(page);
            Assert.False(page.NftLoadFailed);
            Assert.Equal(1, replacement.Closed);
        }
        finally
        {
            previous.ReleaseClose.TrySetResult();
            await refresh.WaitAsync(TimeSpan.FromSeconds(5));
            Leave(page);
        }
    }
}
