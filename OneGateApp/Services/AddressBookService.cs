using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Neo;
using Neo.Wallets;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Properties;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Services;

public sealed partial class AddressBookService(ApplicationDbContext dbContext, ProtocolSettings protocolSettings)
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

    [GeneratedRegex("[A-Za-z0-9_]+", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex AddressTokenPattern();

    public IEnumerable<(int Start, int Length, string Address)> FindAddressOccurrences(string? text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        // Match whole tokens so an address embedded in a longer identifier is not actionable.
        foreach (Match match in AddressTokenPattern().Matches(text))
            if (match.Length == 34 && TryNormalizeAddress(match.Value, out string address))
                yield return (match.Index, match.Length, address);
    }

    public string? ResolveAddress(string? value)
    {
        if (TryNormalizeAddress(value, out string address))
            return address;

        string query = value?.Trim() ?? "";
        if (query.Length == 0) return null;
        string[] matches = dbContext.Contacts
            .AsNoTracking()
            .Where(p => EF.Functions.Collate(p.Label, NoCaseCollation) == query)
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
            .Where(p => EF.Functions.Collate(p.Label, NoCaseCollation) == normalizedLabel);
        if (normalizedAddress is not null)
            matches = matches.Where(p => p.Address != normalizedAddress);
        return !matches.Any();
    }

    public async Task<Contact?> FindByAddressAsync(string address, CancellationToken cancellationToken = default)
    {
        return await dbContext.Contacts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Address == address, cancellationToken);
    }

    public async Task<Contact[]> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Contacts
            .AsNoTracking()
            .OrderBy(p => p.Label == "" ? p.Address : p.Label)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<Contact[]> GetSuggestionsAsync(string? query, int limit = 8)
    {
        string filter = query?.Trim() ?? "";
        IQueryable<Contact> contacts = dbContext.Contacts
            .AsNoTracking();
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

        return result;
    }

    static string EscapeLikePattern(string value)
    {
        return value
            .Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);
    }
}
