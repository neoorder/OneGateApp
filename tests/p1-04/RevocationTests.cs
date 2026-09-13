using NeoOrder.OneGate.Services.RemoteDebug;
using Xunit;

public class RevocationTests
{
    [Fact]
    public async Task RevocationCancelsHandshakeAndRejectsLatePublication()
    {
        using var generation = new RemoteDebugConnectionGeneration();
        var attempt = generation.Capture();
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool published = false;
        async Task FinishHandshake()
        {
            await released.Task;
            generation.ThrowIfStale(attempt);
            published = true;
        }
        Task pending = FinishHandshake();
        generation.Invalidate();
        released.SetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        Assert.True(attempt.Token.IsCancellationRequested);
        Assert.False(published);
    }

    [Fact]
    public void EnablingAgainCannotReactivateAnOldAttempt()
    {
        using var generation = new RemoteDebugConnectionGeneration();
        var old = generation.Capture();
        generation.Invalidate();
        generation.Invalidate();
        Assert.Throws<OperationCanceledException>(() => generation.ThrowIfStale(old));
        generation.ThrowIfStale(generation.Capture());
    }

    [Fact]
    public void InvalidatedAttemptCanStillCreateLinkedCancellationWhileUnwinding()
    {
        using var generation = new RemoteDebugConnectionGeneration();
        var attempt = generation.Capture();
        generation.Invalidate();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(attempt.Token);
        Assert.True(linked.IsCancellationRequested);
    }
}
