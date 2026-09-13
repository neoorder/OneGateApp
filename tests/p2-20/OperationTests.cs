using NeoOrder.OneGate.Services.RemoteDebug;
using Xunit;

public class OperationTests
{
    [Fact]
    public void DisposeRejectsNewOperationsBeforeInvokingTheDelegate()
    {
        var store = new RemoteDebugOperationStore<int>();
        store.Dispose();
        bool started = false;
        Assert.Throws<ObjectDisposedException>(() => store.Start("session", _ => { started = true; return Task.FromResult(1); }));
        Assert.False(started);
    }

    [Fact]
    public void ClearCannotBeReenteredToResurrectAnOperation()
    {
        using var store = new RemoteDebugOperationStore<int>();
        bool rejected = false;
        store.Start("session", token =>
        {
            token.Register(() => rejected = Record.Exception(() => store.Start("session", _ => Task.FromResult(2))) is InvalidOperationException);
            return new TaskCompletionSource<int>().Task;
        });
        store.Clear();
        Assert.True(rejected);
        // Clearing is temporary; a later valid session can start again.
        store.Start("next", _ => Task.FromResult(3));
    }

    [Fact]
    public void StartAndCancellationCallbacksDoNotExecuteUnderTheRegistryLock()
    {
        using var store = new RemoteDebugOperationStore<int>();
        bool cancelledOutsideLock = false;
        bool ReadFromAnotherThread() => Task.Run(() => store.TryGet("missing", "missing", out _)).Wait(TimeSpan.FromSeconds(1));
        store.Start("session", token =>
        {
            Assert.True(ReadFromAnotherThread());
            token.Register(() => cancelledOutsideLock = ReadFromAnotherThread());
            return new TaskCompletionSource<int>().Task;
        });
        store.RemoveSession("session");
        Assert.True(cancelledOutsideLock);
    }

    [Fact]
    public async Task RemovingSessionCancelsItsOperationsAndKeepsOthers()
    {
        using var store = new RemoteDebugOperationStore<int>();
        var pending = new TaskCompletionSource<int>();
        string first = store.Start("first", token => pending.Task.WaitAsync(token));
        string second = store.Start("second", _ => Task.FromResult(42));
        Assert.True(store.TryGet("first", first, out var waiting));
        store.RemoveSession("first");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting!);
        Assert.False(store.TryGet("first", first, out _));
        Assert.False(store.TryGet("first", second, out _));
        Assert.True(store.TryGet("second", second, out var completed));
        Assert.Equal(42, await completed!);
        Assert.False(store.TryGet("second", second, out _));
    }

    [Fact]
    public void CapacityIsCheckedBeforeStartingMoreJavaScript()
    {
        using var store = new RemoteDebugOperationStore<int>(maximumOperations: 1);
        store.Start("session", _ => Task.FromResult(1));
        bool started = false;
        Assert.Throws<InvalidOperationException>(() => store.Start("session", _ => { started = true; return Task.FromResult(2); }));
        Assert.False(started);
        store.Clear();
        store.Start("session", _ => Task.FromResult(3));
    }
}
