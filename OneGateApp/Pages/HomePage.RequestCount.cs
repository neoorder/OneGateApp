using Neo.Wallets;
using NeoOrder.OneGate.Properties;
using System.Globalization;

namespace NeoOrder.OneGate.Pages;

public partial class HomePage
{
    CancellationTokenSource? requestCountCancellation;
    Wallet? requestCountWallet;
    string? requestCountAddress;
    bool requestCountReloadPending;
    long pendingRequestCount;

    string PendingRequestCountText => pendingRequestCount > 99 ? "99+" : pendingRequestCount.ToString(CultureInfo.CurrentCulture);
    public string ContactsSectionTitle => pendingRequestCount > 0 ? $"{Strings.HomeContacts} ({PendingRequestCountText})" : Strings.HomeContacts;
    public string ContactRequestsTitle => pendingRequestCount > 0 ? $"{Strings.ChatRequests} ({PendingRequestCountText})" : Strings.ChatRequests;

    async void RefreshPendingRequestCount()
    {
        if (!isPageVisible) return;
        Wallet? wallet = walletProvider.GetWallet();
        string? address = wallet?.GetDefaultAccount()?.Address;
        if (!ReferenceEquals(wallet, requestCountWallet) || address != requestCountAddress)
        {
            requestCountCancellation?.Cancel();
            requestCountWallet = wallet;
            requestCountAddress = address;
            SetPendingRequestCount(0);
        }
        requestCountReloadPending = true;
        if (requestCountCancellation is not null) return;
        requestCountReloadPending = false;
        using var cancellation = new CancellationTokenSource();
        requestCountCancellation = cancellation;
        try
        {
            var (accountId, count) = await messagingService.GetPendingContactRequestCountAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (isPageVisible && ReferenceEquals(wallet, walletProvider.GetWallet())
                && address == wallet?.GetDefaultAccount()?.Address && accountId == messagingService.AccountId)
                SetPendingRequestCount(count);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Keep the last known count until a later change or page visit retries the local read.
            System.Diagnostics.Debug.WriteLine($"[Meshline] Contact request count refresh failed: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(requestCountCancellation, cancellation))
            {
                requestCountCancellation = null;
                if (requestCountReloadPending && isPageVisible) RefreshPendingRequestCount();
            }
        }
    }

    void SetPendingRequestCount(long count)
    {
        if (pendingRequestCount == count) return;
        pendingRequestCount = count;
        OnPropertyChanged(nameof(ContactsSectionTitle));
        OnPropertyChanged(nameof(ContactRequestsTitle));
    }
}
