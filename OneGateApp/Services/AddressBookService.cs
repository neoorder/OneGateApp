using Microsoft.EntityFrameworkCore;
using Neo;
using Neo.Wallets;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Properties;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Services;

public sealed class AddressBookService(ApplicationDbContext dbContext, ProtocolSettings protocolSettings)
{
    const string NoCaseCollation = "NOCASE";
    const string LikeEscape = "\\";

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
        catch (FormatException)
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
            .Where(p => p.IsAddressBookEntry &&
                EF.Functions.Collate(p.Label, NoCaseCollation) == query)
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
        IQueryable<Contact> matches = dbContext.Contacts
            .AsNoTracking()
            .Where(p => p.IsAddressBookEntry &&
                EF.Functions.Collate(p.Label, NoCaseCollation) == normalizedLabel);
        if (normalizedAddress is not null)
            matches = matches.Where(p => p.Address != normalizedAddress);
        return !matches.Any();
    }

    public async Task<Contact?> FindByAddressAsync(string address)
    {
        return await dbContext.Contacts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Address == address);
    }

    public async Task<Contact[]> GetEntriesAsync()
    {
        return await dbContext.Contacts
            .AsNoTracking()
            .Where(p => p.IsAddressBookEntry)
            .OrderBy(p => p.Label == "" ? p.Address : p.Label)
            .ToArrayAsync();
    }

    public async Task<Contact[]> GetSuggestionsAsync(string? query, int limit = 8)
    {
        string filter = query?.Trim() ?? "";
        IQueryable<Contact> contacts = dbContext.Contacts
            .AsNoTracking()
            .Where(p => p.IsAddressBookEntry);
        Contact[] result;
        if (filter.Length == 0)
        {
            result = await contacts
                .OrderBy(p => p.Label == "" ? p.Address : p.Label)
                .Take(limit)
                .ToArrayAsync();
        }
        else
        {
            string containsPattern = "%" + EscapeLikePattern(filter) + "%";
            string prefixPattern = EscapeLikePattern(filter) + "%";
            result = await contacts
                .Where(p =>
                    EF.Functions.Like(EF.Functions.Collate(p.Address, NoCaseCollation), containsPattern, LikeEscape) ||
                    EF.Functions.Like(EF.Functions.Collate(p.Label, NoCaseCollation), containsPattern, LikeEscape) ||
                    (p.Note != null && EF.Functions.Like(EF.Functions.Collate(p.Note, NoCaseCollation), containsPattern, LikeEscape)))
                .OrderByDescending(p => EF.Functions.Collate(p.Label, NoCaseCollation) == filter)
                .ThenByDescending(p => EF.Functions.Collate(p.Address, NoCaseCollation) == filter)
                .ThenByDescending(p => EF.Functions.Like(EF.Functions.Collate(p.Address, NoCaseCollation), prefixPattern, LikeEscape))
                .ThenBy(p => p.Label == "" ? p.Address : p.Label)
                .Take(limit)
                .ToArrayAsync();
        }

        List<Contact> suggestions = result.ToList();
        if (TryNormalizeAddress(filter, out string normalized) && suggestions.All(p => p.Address != normalized))
        {
            suggestions.Insert(0, new Contact
            {
                Address = normalized,
                Label = "",
                IsAddressBookEntry = false,
                Note = Strings.UseEnteredAddress
            });
        }
        return suggestions.Take(limit).ToArray();
    }

    static string EscapeLikePattern(string value)
    {
        return value
            .Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);
    }
}
