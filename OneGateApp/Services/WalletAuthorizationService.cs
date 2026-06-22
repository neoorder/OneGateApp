using CommunityToolkit.Maui.Extensions;
using Neo.Wallets;
using NeoOrder.OneGate.Controls.Popups;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Properties;

namespace NeoOrder.OneGate.Services;

public class WalletAuthorizationService(IServiceProvider serviceProvider, ApplicationDbContext dbContext, IWalletProvider walletProvider)
{
    public async Task<bool> RequestAuthorizationAsync(Page page, string title, string? message = null, string? domain = null)
    {
        byte[]? credential = await dbContext.Settings.GetAsync<byte[]>("biometric/credential");
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
                var progressPopup = new ProgressPopup
                {
                    Title = title,
                    Message = Strings.UnlockingWallet
                };
                var progressTask = page.ShowPopupAsync<bool>(progressPopup);
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
                finally
                {
                    await progressPopup.CloseAsync(false);
                    await progressTask;
                }
            }
        }
    }
}
