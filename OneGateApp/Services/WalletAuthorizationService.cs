using CommunityToolkit.Maui.Extensions;
using Microsoft.EntityFrameworkCore;
using Neo.Wallets;
using NeoOrder.OneGate.Controls.Popups;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Properties;

namespace NeoOrder.OneGate.Services;

public class WalletAuthorizationService(IServiceProvider serviceProvider, ApplicationDbContext dbContext, IWalletProvider walletProvider)
{
    const int BiometricCredentialId = 1;

    public Task<bool> IsBiometricEnabledAsync() =>
        dbContext.BiometricCredentials.AnyAsync(p => p.Id == BiometricCredentialId);

    public async Task SetBiometricCredentialAsync(byte[] protectedData)
    {
        ArgumentNullException.ThrowIfNull(protectedData);
        await dbContext.Database.ExecuteSqlAsync($"""
            INSERT INTO [BiometricCredentials]([Id], [ProtectedData]) VALUES({BiometricCredentialId}, {protectedData})
            ON CONFLICT([Id]) DO UPDATE SET [ProtectedData] = excluded.[ProtectedData]
            """);
    }

    public async Task<bool> ClearBiometricCredentialAsync() =>
        await dbContext.BiometricCredentials.Where(p => p.Id == BiometricCredentialId).ExecuteDeleteAsync() > 0;

    public async Task<bool> RequestAuthorizationAsync(Page page, string title, string? message = null, string? domain = null)
    {
        byte[]? credential = await dbContext.BiometricCredentials.Where(p => p.Id == BiometricCredentialId)
            .Select(p => p.ProtectedData).FirstOrDefaultAsync();
        if (credential is null)
        {
            var popup = serviceProvider.GetServiceOrCreateInstance<WalletAuthorizationPopup>();
            popup.Title = title;
            popup.Message = message;
            popup.Domain = domain;
            var result = await page.ShowPopupAsync<bool>(popup);
            return result.Result;
        }
        else
        {
            Wallet wallet = walletProvider.GetWallet()
                ?? throw new InvalidOperationException("No wallet available for authorization.");
            if (domain != null)
            {
                message ??= Strings.LoginRequestText;
                message += $"\n{Strings.Domain}: {domain}";
            }
            if (wallet.IsUnlocked)
            {
                return await DataProtectionService.AuthenticateAsync(title, message);
            }
            else
            {
                string password;
                using var progressOverlay = new ProgressWindowOverlay(page.GetParentWindow(), title, Strings.UnlockingWallet);
                try
                {
                    password = await DataProtectionService.UnprotectAsync(credential, title, message);
                    if (!await Task.Run(() => wallet.VerifyPassword(password)))
                        throw new InvalidOperationException("Stored credential is invalid.");
                    return true;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
        }
    }
}
