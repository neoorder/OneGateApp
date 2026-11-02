namespace NeoOrder.OneGate.Pages;

public partial class ChatPage
{
    CancellationTokenSource? readCancellation;
    long lastReadSequence;
    long lastDisplayedSequence;

    async void MarkDisplayedMessagesRead(bool atLatest = false)
    {
        var wallet = walletProvider.GetWallet();
        string? accountId = localAccountId;
        string address = Address;
        bool IsCurrent() => !switchingConversation && IsCurrentMessageContext(wallet, accountId, address);
        if (!IsMessageListPresented || !HasLoaded || IsChatContentCovered || !followLatest || accountId is null || !IsCurrent()) return;
        // Only a confirmed viewport can advance this boundary; loading and write completion cannot.
        if (atLatest) lastDisplayedSequence = Messages.LastOrDefault(message => !message.IsPending)?.LocalSequence ?? 0;
        long sequence = lastDisplayedSequence;
        if (IsLoading || readCancellation is not null || sequence <= lastReadSequence) return;

        using var cancellation = new CancellationTokenSource();
        readCancellation = cancellation;
        bool updated = false;
        try
        {
            if (!await messagingService.MarkChatReadAsync(accountId, address, sequence, cancellation.Token)) return;
            cancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrent()) return;
            lastReadSequence = sequence;
            updated = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested && IsCurrent()) ErrorText ??= ex.Message;
        }
        finally
        {
            if (ReferenceEquals(readCancellation, cancellation))
            {
                readCancellation = null;
                // Coalesce displayed arrivals while a write was in progress, without retrying a failed write in a loop.
                if (updated) MarkDisplayedMessagesRead();
            }
        }
    }

    void ClearReadState()
    {
        readCancellation?.Cancel();
        readCancellation = null;
        lastReadSequence = 0;
        lastDisplayedSequence = 0;
    }
}
