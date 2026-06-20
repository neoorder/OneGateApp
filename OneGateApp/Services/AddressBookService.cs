using Microsoft.EntityFrameworkCore;
using Neo;
using Neo.Wallets;
using NeoOrder.OneGate.Data;
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
        string[] matches = dbContext.Contacts
            .AsNoTracking()
            .Where(p => p.IsAddressBookEntry)
            .AsEnumerable()
            .Where(p => string.Equals(p.Label, query, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Address)
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public bool IsLabelAvailable(string? label, string? currentAddress = null)
    {
        string normalizedLabel = label?.Trim() ?? "";
        if (normalizedLabel.Length == 0) return true;
        string? normalizedAddress = TryNormalizeAddress(currentAddress, out string address) ? address : null;
        return !dbContext.Contacts
            .AsNoTracking()
            .Where(p => p.IsAddressBookEntry)
            .AsEnumerable()
            .Any(p =>
                !string.Equals(p.Address, normalizedAddress, StringComparison.Ordinal) &&
                string.Equals(p.Label, normalizedLabel, StringComparison.OrdinalIgnoreCase));
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
            .Where(p => p.IsAddressBookEntry)
            .ToArrayAsync();
        return contacts
            .OrderBy(p => p.DisplayName)
            .ToArray();
    }

    public async Task<Contact[]> GetSuggestionsAsync(string? query, int limit = 8)
    {
        Contact[] contacts = await dbContext.Contacts
            .AsNoTracking()
            .Where(p => p.IsAddressBookEntry)
            .ToArrayAsync();

        IEnumerable<Contact> filtered;
        string filter = query?.Trim() ?? "";
        if (filter.Length == 0)
        {
            filtered = contacts
                .OrderBy(p => p.DisplayName);
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
}
