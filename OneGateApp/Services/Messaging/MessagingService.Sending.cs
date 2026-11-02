using Meshline;
using Meshline.Models.Client;
using Neo.Wallets;
using NeoOrder.OneGate.Properties;

namespace NeoOrder.OneGate.Services.Messaging;

public sealed partial class MessagingService
{
    async Task SubmitTextMessageAsync(PendingChatMessage message, CancellationToken cancellationToken)
    {
        lock (pendingLock)
            if (!pendingMessages.TryGetValue(message.Id, out var queued) || queued.IsCanceled) return;
        MeshlineClient current;
        Wallet wallet;
        WalletAccount account;
        string recipient;
        CancellationTokenSource operation;
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await InitializeCoreAsync(cancellationToken);
            ValidatePendingAccount(message.LocalAccountId);
            current = client!;
            wallet = sessionWallet!;
            account = sessionAccount!;
            recipient = GetContactAccountId(message.Address);
            operation = CreateOperation(cancellationToken);
            try
            {
                if (!(await ReadChatContactAsync(recipient, operation.Token)).IsContact)
                    throw new InvalidOperationException(Strings.ChatContactRequired);
                if (message.ReplyTo is { } reference)
                {
                    // A restored draft must still refer to an original from this conversation.
                    var original = await current.MessageManager.GetMessageAsync(reference, operation.Token);
                    if (original is null || original.Key != reference
                        || !(original.Key.Sender == message.LocalAccountId && original.Recipient == recipient
                            || original.Key.Sender == recipient && original.Recipient == message.LocalAccountId))
                        throw new InvalidOperationException(Strings.ChatQuotedMessageUnavailable);
                }
                operation.Token.ThrowIfCancellationRequested();
                EnsureCurrentAccount(wallet, account);
            }
            catch { operation.Dispose(); throw; }
        }
        finally { lifecycleLock.Release(); }

        using (operation)
        {
            void OnWalletChanged(object? sender, Wallet? changedWallet)
            {
                if (!ReferenceEquals(changedWallet, wallet)) operation.Cancel();
            }
            walletProvider.WalletChanged += OnWalletChanged;
            try
            {
                operation.Token.ThrowIfCancellationRequested();
                EnsureCurrentAccount(wallet, account);
                if (!ReferenceEquals(current, client)) throw new OperationCanceledException(operation.Token);
                // Local cancellation and handing the message to the SDK must have a single winner.
                lock (pendingLock)
                {
                    if (!pendingMessages.TryGetValue(message.Id, out var queued) || !queued.CanCancelSubmission) return;
                    pendingMessages[message.Id] = queued with { HasSubmissionStarted = true };
                }
                NotifyChanged();
                // Preparing a send can use the network. Only this conversation waits for its correlation.
                var status = await current.MessageManager.SendMessageAsync(recipient,
                    new DirectMessageDraft
                    {
                        Body = new() { ContentType = "text/plain", Text = message.Text }, ReplyTo = message.ReplyTo
                    }, operation.Token);
                lock (pendingLock)
                    if (pendingMessages.TryGetValue(message.Id, out var queued))
                        pendingMessages[message.Id] = queued with { SendStatus = status };
            }
            finally { walletProvider.WalletChanged -= OnWalletChanged; }
        }
    }

    public async Task<Dictionary<string, MessageSendStatus>> GetChatSendStatusesAsync(string localAccountId, string address,
        CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await InitializeCoreAsync(cancellationToken);
            if (!IsEnabled) throw new InvalidOperationException(Strings.ChatEnableFirst);
            if (AccountId != localAccountId) throw new InvalidOperationException(Strings.ChatAccountChanged);
            string recipient = GetContactAccountId(address);
            using var operation = CreateOperation(cancellationToken);
            var result = new Dictionary<string, MessageSendStatus>(StringComparer.Ordinal);
            await using var reader = await client!.MessageManager.GetOutboxAsync(recipient, cancellationToken: operation.Token);
            while (await reader.ReadNextAsync(100, operation.Token) is { Count: > 0 } batch)
                foreach (var status in batch) result[status.MessageId] = status;
            operation.Token.ThrowIfCancellationRequested();
            EnsureCurrentAccount(sessionWallet!, sessionAccount!);
            return result;
        }
        finally { lifecycleLock.Release(); }
    }
}
