using Neo;
using Neo.Wallets;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;

namespace NeoOrder.OneGate.Services;

public class ConnectedDAppService(ApplicationDbContext dbContext, IWalletProvider walletProvider, ProtocolSettings protocolSettings)
{
    const string SettingsKeyPrefix = "dapps/connected";

    public async Task<ConnectedDApp[]> LoadAsync()
    {
        string? settingsKey = GetSettingsKey();
        if (settingsKey is null) return [];

        ConnectedDApp[] apps = await dbContext.Settings.GetAsync<ConnectedDApp[]>(settingsKey) ?? [];
        return apps
            .Where(p => !string.IsNullOrWhiteSpace(p.Domain))
            .OrderByDescending(p => p.LastUsedAt)
            .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task<bool> IsConnectedAsync(string domain)
    {
        domain = NormalizeDomain(domain);
        ConnectedDApp[] apps = await LoadAsync();
        return apps.Any(p => string.Equals(p.Domain, domain, StringComparison.OrdinalIgnoreCase));
    }

    public async Task ConnectAsync(string domain, string? name)
    {
        string? settingsKey = GetSettingsKey();
        if (settingsKey is null) return;

        domain = NormalizeDomain(domain);
        if (string.IsNullOrWhiteSpace(domain)) return;

        List<ConnectedDApp> apps = [.. await LoadAsync()];
        ConnectedDApp? app = apps.FirstOrDefault(p => string.Equals(p.Domain, domain, StringComparison.OrdinalIgnoreCase));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (app is null)
        {
            apps.Add(new ConnectedDApp
            {
                Domain = domain,
                Name = string.IsNullOrWhiteSpace(name) ? null : name,
                ConnectedAt = now,
                LastUsedAt = now
            });
        }
        else
        {
            app.Name = string.IsNullOrWhiteSpace(name) ? app.Name : name;
            app.LastUsedAt = now;
        }

        await dbContext.Settings.PutAsync(settingsKey, apps.OrderBy(p => p.Domain).ToArray());
    }

    public async Task DisconnectAsync(string domain)
    {
        string? settingsKey = GetSettingsKey();
        if (settingsKey is null) return;

        domain = NormalizeDomain(domain);
        ConnectedDApp[] apps = await LoadAsync();
        ConnectedDApp[] remaining = apps
            .Where(p => !string.Equals(p.Domain, domain, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (remaining.Length == 0)
            await dbContext.Settings.DeleteAsync(settingsKey);
        else
            await dbContext.Settings.PutAsync(settingsKey, remaining);
    }

    public async Task DisconnectAllAsync()
    {
        string? settingsKey = GetSettingsKey();
        if (settingsKey is null) return;

        await dbContext.Settings.DeleteAsync(settingsKey);
    }

    string? GetSettingsKey()
    {
        WalletAccount? account = walletProvider.GetWallet()?.GetDefaultAccount();
        if (account is null) return null;

        string address = account.ScriptHash.ToAddress(protocolSettings.AddressVersion);
        return $"{SettingsKeyPrefix}/{address}";
    }

    static string NormalizeDomain(string domain)
    {
        return domain.Trim().TrimEnd('.').ToLowerInvariant();
    }
}
