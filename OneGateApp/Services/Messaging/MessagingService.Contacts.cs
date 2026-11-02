using Meshline.Identity;
using Meshline.Models.Client;
using Meshline.Models.Protocol;
using Meshline;
using Neo.Wallets;
using NeoOrder.OneGate.Properties;
using System.Globalization;
using System.Text;

namespace NeoOrder.OneGate.Services.Messaging;

public sealed partial class MessagingService
{
    // Serialize contact changes without blocking message submission or local reads.
    readonly SemaphoreSlim contactMutationLock = new(1, 1);
    long chatContactsRevision;
    long cachedRecipientsRevision = -1;
    string[] cachedRecipients = [];
    public long ChatContactsRevision => Interlocked.Read(ref chatContactsRevision);

    /// <summary>Reads active Neo chat contacts from local storage, including those without conversation history.</summary>
    public async Task<(string LocalAccountId, string[] Contacts)> GetChatRecipientsAsync(
        CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await InitializeCoreAsync(cancellationToken);
            if (!IsEnabled) return (AccountId!, []);
            using var operation = CreateOperation(cancellationToken);
            operation.Token.ThrowIfCancellationRequested();
            EnsureCurrentAccount(sessionWallet!, sessionAccount!);
            long revision = ChatContactsRevision;
            if (cachedRecipientsRevision == revision) return (AccountId!, cachedRecipients);
            string prefix = $"neo:{registry.Context.Reference.ToString(CultureInfo.InvariantCulture)}:";
            var contacts = new HashSet<string>(StringComparer.Ordinal);
            await using var reader = await client!.MessageManager.GetContactsAsync(cancellationToken: operation.Token);
            while (await reader.ReadNextAsync(100, operation.Token) is { Count: > 0 } batch)
                foreach (var contact in batch)
                    if (contact.State == ContactRelationshipState.Active && contact.AccountId != AccountId
                        && contact.AccountId.StartsWith(prefix, StringComparison.Ordinal)
                        && AccountAdapter.ValidateAccountId(contact.AccountId) is null)
                        contacts.Add(contact.AccountId[prefix.Length..]);
            operation.Token.ThrowIfCancellationRequested();
            EnsureCurrentAccount(sessionWallet!, sessionAccount!);
            // Contact changes and session closure invalidate this snapshot. A change during
            // the read keeps it dirty for the next caller without adding a refresh timer.
            cachedRecipients = contacts.ToArray();
            cachedRecipientsRevision = revision;
            return (AccountId!, cachedRecipients);
        }
        finally { lifecycleLock.Release(); }
    }

    public async Task<Dictionary<string, (string Status, bool CanChat, bool HasIncomingRequest)>> GetContactStatusesAsync(CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await InitializeCoreAsync(cancellationToken);
            var result = new Dictionary<string, (string Status, bool CanChat, bool HasIncomingRequest)>(StringComparer.Ordinal);
            if (!IsEnabled) return result;
            string prefix = $"neo:{registry.Context.Reference.ToString(CultureInfo.InvariantCulture)}:";
            await using (var requests = await client!.MessageManager.GetContactRequestsAsync(
                cancellationToken: cancellationToken))
            {
                while (await requests.ReadNextAsync(100, cancellationToken) is { Count: > 0 } batch)
                    foreach (var request in batch.Where(p => p.AccountId != AccountId
                        && p.AccountId.StartsWith(prefix, StringComparison.Ordinal)
                        && AccountAdapter.ValidateAccountId(p.AccountId) is null))
                    {
                        string address = request.AccountId[(request.AccountId.LastIndexOf(':') + 1)..];
                        // A received request takes priority when both accounts have sent an application.
                        if (request.Direction == ContactRequestDirection.Incoming || !result.ContainsKey(address))
                            result[address] = (ChatContactDetails.GetStatus(null, request), false, request.Direction == ContactRequestDirection.Incoming);
                    }
            }
            await using (var contacts = await client.MessageManager.GetContactsAsync(cancellationToken: cancellationToken))
            {
                while (await contacts.ReadNextAsync(100, cancellationToken) is { Count: > 0 } batch)
                    foreach (var contact in batch.Where(p => p.State == ContactRelationshipState.Active && p.AccountId != AccountId
                        && p.AccountId.StartsWith(prefix, StringComparison.Ordinal)
                        && AccountAdapter.ValidateAccountId(p.AccountId) is null))
                        result[contact.AccountId[prefix.Length..]] = (ChatContactDetails.GetStatus(contact, null), true, false);
            }
            EnsureCurrentAccount(sessionWallet!, sessionAccount!);
            return result;
        }
        finally { lifecycleLock.Release(); }
    }

    public async Task<ChatContactDetails> GetChatContactAsync(string address, bool resolveProfile = true,
        CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? operation = null;
        try
        {
            ChatContactDetails details;
            await lifecycleLock.WaitAsync(cancellationToken);
            try
            {
                await InitializeCoreAsync(cancellationToken);
                if (!IsEnabled) throw new InvalidOperationException(Strings.ChatEnableFirst);
                operation = CreateOperation(cancellationToken);
                string accountId = GetContactAccountId(address);
                details = await ReadChatContactAsync(accountId, operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                EnsureCurrentAccount(sessionWallet!, sessionAccount!);
            }
            finally { lifecycleLock.Release(); }

            if (resolveProfile && details.CanCreateRequest)
            {
                // A cold address lookup must not hold up message submission or local chat reads.
                // Reuse the shared profile lookup and keep cancellation tied to the original session.
                var profile = await GetChatProfileAsync(details.LocalAccountId, address, operation.Token);
                details = details with { Profile = profile };
            }
            return details;
        }
        finally { operation?.Dispose(); }
    }

    public async Task<ChatContactDetails> SendContactRequestAsync(string localAccountId, string address, string? note,
        CancellationToken cancellationToken = default, string? invitation = null)
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
            ContactInvite? invite = invitation is null ? null : ParseChatInvitation(invitation);
            if (invite is not null && invite.Inviter != accountId)
                throw new ArgumentException(Strings.ChatInvalidInvitation, nameof(invitation));
            note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            if (note is not null && Encoding.UTF8.GetByteCount(note) > 1024)
                throw new ArgumentException(Strings.ChatRequestNoteTooLong, nameof(note));

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
                ChatContactDetails details = await ReadChatContactAsync(accountId, operation.Token);
                if (details.IsContact) return details;
                if (details.CanCreateRequest)
                {
                    if (invite is null)
                    {
                        AccountProfile? profile = await RunContactNetworkOperationAsync(current, wallet, account,
                            () => current.ProfileManager.GetProfileAsync(accountId, operation.Token), operation.Token);
                        details = await ReadChatContactAsync(accountId, operation.Token);
                        if (!details.CanCreateRequest) return details;
                        if (profile is not { PublicDiscovery: true })
                            throw new InvalidOperationException(Strings.ChatAddressNotDiscoverable);
                    }
                    // The unified contact list reads outgoing requests directly from SDK state.
                    operation.Token.ThrowIfCancellationRequested();
                    EnsureCurrentAccount(wallet, account);
                    if (invite is null)
                        await RunContactNetworkOperationAsync(current, wallet, account,
                            () => current.MessageManager.AddContactAsync(accountId, note, operation.Token), operation.Token);
                    else
                        await RunContactNetworkOperationAsync(current, wallet, account,
                            () => current.MessageManager.AddContactAsync(invite, note, operation.Token), operation.Token);
                }

                EnsureCurrentAccount(wallet, account);
                return await ReadChatContactAsync(accountId, operation.Token);
            }
            finally { walletProvider.WalletChanged -= OnWalletChanged; }
        }
        finally
        {
            if (hasLifecycleLock) lifecycleLock.Release();
            contactMutationLock.Release();
        }
    }

    // Caller holds both locks. Always reacquire lifecycleLock before returning or throwing,
    // so its local database work and finally block retain the same lock ownership.
    async Task<T> RunContactNetworkOperationAsync<T>(MeshlineClient current, Wallet wallet, WalletAccount account,
        Func<Task<T>> action, CancellationToken cancellationToken)
    {
        T result;
        lifecycleLock.Release();
        try
        {
            result = await action();
        }
        finally { await lifecycleLock.WaitAsync(); }

        cancellationToken.ThrowIfCancellationRequested();
        EnsureCurrentAccount(wallet, account);
        if (!ReferenceEquals(current, client) || !IsEnabled)
            throw new OperationCanceledException(cancellationToken);
        return result;
    }

    string GetContactAccountId(string address)
    {
        string accountId = $"neo:{registry.Context.Reference.ToString(CultureInfo.InvariantCulture)}:{address.Trim()}";
        if (AccountAdapter.ValidateAccountId(accountId) is not null)
            throw new ArgumentException(Strings.ChatInvalidAddress, nameof(address));
        if (accountId == AccountId) throw new ArgumentException(Strings.ChatCannotAddSelf, nameof(address));
        return accountId;
    }

    async Task<ChatContactDetails> ReadChatContactAsync(string accountId, CancellationToken cancellationToken)
    {
        ContactInfo? contact = await client!.MessageManager.GetContactAsync(accountId, cancellationToken);
        await using var requests = await client.MessageManager.GetContactRequestsAsync(accountId,
            cancellationToken: cancellationToken);
        // Local storage has at most one request per account and direction.
        var pending = await requests.ReadNextAsync(2, cancellationToken);
        ContactRequestInfo? request = pending.FirstOrDefault(p => p.Direction == ContactRequestDirection.Incoming)
            ?? pending.FirstOrDefault(p => p.Direction == ContactRequestDirection.Outgoing);
        return new(AccountId!, accountId[(accountId.LastIndexOf(':') + 1)..], null, contact, request);
    }

    /// <summary>Mirrors an address-book name to an active chat contact; names are always read from the address book.</summary>
    public async Task SetChatContactAliasAsync(string localAccountId, string address, string? label,
        CancellationToken cancellationToken = default)
    {
        await contactMutationLock.WaitAsync(cancellationToken);
        try
        {
            await lifecycleLock.WaitAsync(cancellationToken);
            try
            {
                if (!IsEnabled) throw new InvalidOperationException(Strings.ChatEnableFirst);
                if (AccountId != localAccountId) throw new InvalidOperationException(Strings.ChatAccountChanged);
                var current = client!;
                Wallet wallet = sessionWallet!;
                WalletAccount account = sessionAccount!;
                EnsureCurrentAccount(wallet, account);
                if (address == account.Address) return;
                string peer = GetContactAccountId(address);
                using var operation = CreateOperation(cancellationToken);
                void OnWalletChanged(object? sender, Wallet? changedWallet)
                {
                    if (!ReferenceEquals(changedWallet, wallet)) operation.Cancel();
                }
                walletProvider.WalletChanged += OnWalletChanged;
                try
                {
                    var contact = await current.MessageManager.GetContactAsync(peer, operation.Token);
                    operation.Token.ThrowIfCancellationRequested();
                    EnsureCurrentAccount(wallet, account);
                    if (contact?.State != ContactRelationshipState.Active) return;
                    string? alias = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
                    // The SDK commits the alias and queues device synchronization without waiting for delivery.
                    await RunContactNetworkOperationAsync(current, wallet, account,
                        () => current.MessageManager.SetAliasAsync(peer, alias, operation.Token), operation.Token);
                }
                finally { walletProvider.WalletChanged -= OnWalletChanged; }
            }
            finally { lifecycleLock.Release(); }
        }
        finally { contactMutationLock.Release(); }
    }

    public async Task RemoveChatContactAsync(string localAccountId, string address, CancellationToken cancellationToken = default)
    {
        await contactMutationLock.WaitAsync(cancellationToken);
        bool hasLifecycleLock = false;
        try
        {
            await lifecycleLock.WaitAsync(cancellationToken);
            hasLifecycleLock = true;
            if (!IsEnabled) throw new InvalidOperationException(Strings.ChatEnableFirst);
            if (AccountId != localAccountId) throw new InvalidOperationException(Strings.ChatAccountChanged);
            string peer = GetContactAccountId(address);
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
                operation.Token.ThrowIfCancellationRequested();
                EnsureCurrentAccount(wallet, account);
                // SDK removal keeps history and queues the relationship change for the account's other devices.
                await client!.MessageManager.RemoveContactAsync(peer, operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                EnsureCurrentAccount(wallet, account);
            }
            finally
            {
                walletProvider.WalletChanged -= OnWalletChanged;
                // Also reread local state when an error follows a committed removal.
                NotifyChanged();
            }
        }
        finally
        {
            if (hasLifecycleLock) lifecycleLock.Release();
            contactMutationLock.Release();
        }
    }
}
