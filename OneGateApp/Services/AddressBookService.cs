using Microsoft.EntityFrameworkCore;
using Neo;
using Neo.Wallets;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Pages;
using NeoOrder.OneGate.Properties;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Services;

public sealed class AddressBookService(ApplicationDbContext dbContext, ProtocolSettings protocolSettings)
{
    public bool TryNormalizeAddress(string? value, out string address)
    {
        address = "";
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            UInt160 hash = value.Trim().ToScriptHash(protocolSettings.AddressVersion);
            address = hash.ToAddress(protocolSettings.AddressVersion);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string? ResolveAddress(string? value)
    {
        if (TryNormalizeAddress(value, out string address))
            return address;

        string query = value?.Trim() ?? "";
        if (query.Length == 0) return null;
        return dbContext.Contacts
            .AsNoTracking()
            .Where(p => p.IsAddressBookEntry)
            .AsEnumerable()
            .Where(p => string.Equals(p.Label, query, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Address)
            .FirstOrDefault();
    }

    public async Task<Contact?> FindByAddressAsync(string address)
    {
        return await dbContext.Contacts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Address == address);
    }

    public async Task<Contact[]> GetEntriesAsync()
    {
        Contact[] contacts = await dbContext.Contacts
            .AsNoTracking()
            .Where(p => p.IsAddressBookEntry || p.TransferCount > 0)
            .ToArrayAsync();
        return contacts
            .OrderByDescending(p => p.IsAddressBookEntry)
            .ThenByDescending(p => p.LastUsedAt ?? DateTimeOffset.MinValue)
            .ThenBy(p => p.DisplayName)
            .ToArray();
    }

    public async Task<Contact[]> GetSuggestionsAsync(string? query, int limit = 8)
    {
        Contact[] contacts = await dbContext.Contacts
            .AsNoTracking()
            .Where(p => p.IsAddressBookEntry || p.TransferCount > 0)
            .ToArrayAsync();

        IEnumerable<Contact> filtered;
        string filter = query?.Trim() ?? "";
        if (filter.Length == 0)
        {
            filtered = contacts
                .Where(p => p.LastUsedAt is not null || p.IsAddressBookEntry)
                .OrderByDescending(p => p.LastUsedAt ?? DateTimeOffset.MinValue)
                .ThenByDescending(p => p.IsAddressBookEntry)
                .ThenBy(p => p.DisplayName);
        }
        else
        {
            filtered = contacts
                .Where(p =>
                    p.Address.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    p.Label.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    (p.Note?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
                .OrderByDescending(p => string.Equals(p.Label, filter, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(p => p.Address.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(p => p.LastUsedAt ?? DateTimeOffset.MinValue)
                .ThenBy(p => p.DisplayName);
        }

        List<Contact> result = filtered.Take(limit).ToList();
        if (TryNormalizeAddress(filter, out string normalized) && result.All(p => p.Address != normalized))
        {
            result.Insert(0, new Contact
            {
                Address = normalized,
                Label = "",
                IsAddressBookEntry = false,
                Note = Strings.UseEnteredAddress
            });
        }
        return result.Take(limit).ToArray();
    }

    public async Task<ContactTransfer[]> GetTransfersAsync(string address)
    {
        return await dbContext.ContactTransfers
            .AsNoTracking()
            .Where(p => p.Address == address)
            .OrderByDescending(p => p.CreatedAt)
            .ToArrayAsync();
    }

    public async Task RecordTransferAsync(string address, string transactionHash, string? assetSymbol, string? amount, string? memo = null)
    {
        if (!TryNormalizeAddress(address, out string normalized))
            normalized = address;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Contact? contact = await dbContext.Contacts.FirstOrDefaultAsync(p => p.Address == normalized);
        if (contact is null)
        {
            dbContext.Contacts.Add(new Contact
            {
                Address = normalized,
                Label = "",
                IsAddressBookEntry = false,
                LastUsedAt = now,
                TransferCount = 1,
                LastTransactionHash = transactionHash
            });
        }
        else
        {
            contact.LastUsedAt = now;
            contact.TransferCount++;
            contact.LastTransactionHash = transactionHash;
        }

        bool exists = await dbContext.ContactTransfers.AnyAsync(p => p.TransactionHash == transactionHash && p.Address == normalized);
        if (!exists)
        {
            dbContext.ContactTransfers.Add(new ContactTransfer
            {
                Address = normalized,
                TransactionHash = transactionHash,
                CreatedAt = now,
                AssetSymbol = assetSymbol,
                Amount = amount,
                Memo = memo
            });
        }

        await dbContext.SaveChangesAsync();
        GlobalStates.Invalidate<ContactsPage>();
    }
}
