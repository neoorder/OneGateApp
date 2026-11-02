using Meshline.Components;
using Meshline.Identity;
using Meshline.Models.Client;
using Meshline.Models.Protocol;
using Meshline;
using Neo.Wallets;
using NeoOrder.OneGate.Properties;
using System.Globalization;

namespace NeoOrder.OneGate.Services.Messaging;

public sealed partial class MessagingService
{
    public async Task<(string LocalAccountId, ChatContactDetails[] Requests)> GetContactRequestsAsync(
        CancellationToken cancellationToken = default)
    {
        MeshlineClient current;
        Wallet wallet;
        WalletAccount account;
        CancellationTokenSource operation;
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await InitializeCoreAsync(cancellationToken);
            if (!IsEnabled) throw new InvalidOperationException(Strings.ChatEnableFirst);
            current = client!;
            wallet = sessionWallet!;
            account = sessionAccount!;
            EnsureCurrentAccount(wallet, account);
            operation = CreateOperation(cancellationToken);
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
                await lifecycleLock.WaitAsync(operation.Token);
                try
                {
                    operation.Token.ThrowIfCancellationRequested();
                    EnsureCurrentAccount(wallet, account);
                    if (!ReferenceEquals(current, client) || !IsEnabled)
                        throw new OperationCanceledException(operation.Token);

                    string prefix = $"neo:{registry.Context.Reference.ToString(CultureInfo.InvariantCulture)}:";
                    var requests = new List<ContactRequestInfo>();
                    await using (var reader = await current.MessageManager.GetContactRequestsAsync(cancellationToken: operation.Token))
                    {
                        while (await reader.ReadNextAsync(100, operation.Token) is { Count: > 0 } batch)
                            requests.AddRange(batch.Where(p => p.AccountId.StartsWith(prefix, StringComparison.Ordinal)
                                && p.AccountId != AccountId && AccountAdapter.ValidateAccountId(p.AccountId) is null));
                    }

                    var contacts = new Dictionary<string, ContactInfo>(StringComparer.Ordinal);
                    await using (var reader = await current.MessageManager.GetContactsAsync(cancellationToken: operation.Token))
                    {
                        while (await reader.ReadNextAsync(100, operation.Token) is { Count: > 0 } batch)
                            foreach (var contact in batch) contacts[contact.AccountId] = contact;
                    }
                    operation.Token.ThrowIfCancellationRequested();
                    EnsureCurrentAccount(wallet, account);
                    return (AccountId!, requests.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.AccountId, StringComparer.Ordinal)
                        .Select(request => new ChatContactDetails(AccountId!, request.AccountId[prefix.Length..], null,
                            contacts.GetValueOrDefault(request.AccountId), request)).ToArray());
                }
                finally { lifecycleLock.Release(); }
            }
            finally { walletProvider.WalletChanged -= OnWalletChanged; }
        }
    }

    public async Task RespondToContactRequestAsync(string localAccountId, string address,
        string? messageId, DateTimeOffset createdAt, bool accept, CancellationToken cancellationToken = default)
    {
        await contactMutationLock.WaitAsync(cancellationToken);
        bool hasLifecycleLock = false;
        try
        {
            await lifecycleLock.WaitAsync(cancellationToken);
            hasLifecycleLock = true;
            await InitializeCoreAsync(cancellationToken);
            if (!IsEnabled) throw new InvalidOperationException(Strings.ChatEnableFirst);
            if (AccountId != localAccountId) throw new InvalidOperationException(Strings.ChatAccountChanged);
            string accountId = GetContactAccountId(address);
            var current = client!;
            Wallet wallet = sessionWallet!;
            WalletAccount account = sessionAccount!;
            using var operation = CreateOperation(cancellationToken);
            void OnWalletChanged(object? sender, Wallet? current)
            {
                if (!ReferenceEquals(current, wallet)) operation.Cancel();
            }
            walletProvider.WalletChanged += OnWalletChanged;
            try
            {
                EnsureCurrentAccount(wallet, account);
                ContactRequestInfo? request;
                await using (var reader = await client!.MessageManager.GetContactRequestsAsync(accountId,
                    ContactRequestDirection.Incoming, operation.Token))
                    request = (await reader.ReadNextAsync(1, operation.Token)).FirstOrDefault();

                if (request is null)
                {
                    if (!accept) return;
                    var contact = await client.MessageManager.GetContactAsync(accountId, operation.Token);
                    if (contact?.State != ContactRelationshipState.Active)
                        throw new InvalidOperationException(Strings.ChatRequestChanged);
                }
                else if (request.MessageId != messageId || request.CreatedAt != createdAt)
                {
                    throw new InvalidOperationException(Strings.ChatRequestChanged);
                }

                if (accept)
                {
                    // Accepted friends appear in the unified list without saving a transfer address.
                    operation.Token.ThrowIfCancellationRequested();
                    EnsureCurrentAccount(wallet, account);
                    if (request is not null)
                        await RunContactNetworkOperationAsync(current, wallet, account,
                            () => current.MessageManager.AcceptContactRequestAsync(accountId, operation.Token), operation.Token);
                }
                else
                {
                    operation.Token.ThrowIfCancellationRequested();
                    EnsureCurrentAccount(wallet, account);
                    await client.MessageManager.DismissContactRequestAsync(accountId, operation.Token);
                }
                EnsureCurrentAccount(wallet, account);
            }
            finally { walletProvider.WalletChanged -= OnWalletChanged; }
        }
        finally
        {
            if (hasLifecycleLock) lifecycleLock.Release();
            contactMutationLock.Release();
        }
    }

    long requestCountRevision;
    long cachedRequestCountRevision = -1;
    long cachedRequestCount;

    void OnContactStateChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, client?.MessageManager)) return;
        // Search includes outgoing requests as well as established contacts.
        if (e is not ContactChangedEventArgs change
            || (change.ChangeKind & (ContactChangeKind.Relationship | ContactChangeKind.Deleted)) != 0)
            Interlocked.Increment(ref chatContactsRevision);
        Interlocked.Increment(ref requestCountRevision);
        NotifyChanged();
    }

    /// <summary>Reads pending incoming requests from local storage, without starting a session or synchronizing relays.</summary>
    public async Task<(string? LocalAccountId, long Count)> GetPendingContactRequestCountAsync(CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsEnabled) return (AccountId, 0);
            using var operation = CreateOperation(cancellationToken);
            operation.Token.ThrowIfCancellationRequested();
            EnsureCurrentAccount(sessionWallet!, sessionAccount!);
            long revision = Volatile.Read(ref requestCountRevision);
            if (cachedRequestCountRevision == revision) return (AccountId, cachedRequestCount);

            string prefix = $"neo:{registry.Context.Reference.ToString(CultureInfo.InvariantCulture)}:";
            var incoming = new HashSet<string>(StringComparer.Ordinal);
            await using (var reader = await client!.MessageManager.GetContactRequestsAsync(
                direction: ContactRequestDirection.Incoming, cancellationToken: operation.Token))
            {
                while (await reader.ReadNextAsync(100, operation.Token) is { Count: > 0 } batch)
                    foreach (var request in batch)
                        if (request.AccountId != AccountId && request.AccountId.StartsWith(prefix, StringComparison.Ordinal)
                            && AccountAdapter.ValidateAccountId(request.AccountId) is null)
                            incoming.Add(request.AccountId);
            }
            if (incoming.Count > 0)
            {
                await using var reader = await client.MessageManager.GetContactsAsync(cancellationToken: operation.Token);
                while (await reader.ReadNextAsync(100, operation.Token) is { Count: > 0 } batch)
                    foreach (var contact in batch)
                        if (contact.State == ContactRelationshipState.Active) incoming.Remove(contact.AccountId);
            }
            operation.Token.ThrowIfCancellationRequested();
            EnsureCurrentAccount(sessionWallet!, sessionAccount!);
            // A change during this read leaves the cache dirty for the next request.
            cachedRequestCount = incoming.Count;
            cachedRequestCountRevision = revision;
            return (AccountId, cachedRequestCount);
        }
        finally { lifecycleLock.Release(); }
    }
}
