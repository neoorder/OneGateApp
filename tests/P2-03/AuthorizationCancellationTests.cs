using Neo;
using Neo.Wallets;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Services;
using Xunit;

public class AuthorizationCancellationTests
{
    [Fact]
    public async Task Unlocked_wallet_cancelled_biometrics_returns_false()
    {
        using var fixture = new WalletFixture(locked: false);
        DataProtectionService.Authenticate = () => Task.FromCanceled<bool>(new CancellationToken(true));
        Assert.False(await fixture.Service.RequestAuthorizationAsync(new Page(), "Export"));
    }

    [Fact]
    public async Task Locked_wallet_cancelled_biometrics_returns_false_and_stays_locked()
    {
        using var fixture = new WalletFixture(locked: true);
        DataProtectionService.Unprotect = () => Task.FromCanceled<string>(new CancellationToken(true));
        Assert.False(await fixture.Service.RequestAuthorizationAsync(new Page(), "Send"));
        Assert.False(fixture.Wallet.IsUnlocked);
    }

    [Fact]
    public async Task Rejected_biometrics_does_not_authorize()
    {
        using var fixture = new WalletFixture(locked: false);
        DataProtectionService.Authenticate = () => Task.FromResult(false);
        Assert.False(await fixture.Service.RequestAuthorizationAsync(new Page(), "Send"));
    }

    [Fact]
    public async Task Successful_biometrics_authorizes_both_wallet_states()
    {
        using var fixture = new WalletFixture(locked: true);
        DataProtectionService.Unprotect = () => Task.FromResult(WalletFixture.Password);
        Assert.True(await fixture.Service.RequestAuthorizationAsync(new Page(), "Export"));
        Assert.True(fixture.Wallet.IsUnlocked);
        DataProtectionService.Authenticate = () => Task.FromResult(true);
        Assert.True(await fixture.Service.RequestAuthorizationAsync(new Page(), "Export"));
    }

    [Fact]
    public async Task System_failure_is_not_silently_converted_to_user_cancellation()
    {
        using var fixture = new WalletFixture(locked: false);
        DataProtectionService.Authenticate = () => Task.FromException<bool>(new InvalidOperationException("Biometric hardware unavailable"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.RequestAuthorizationAsync(new Page(), "Export"));
    }
}

sealed class WalletFixture : IWalletProvider, IDisposable
{
    public const string Password = "disposable-authorization-password";
    readonly string directory = Directory.CreateTempSubdirectory("onegate-authorization-test-").FullName;
    public Wallet Wallet { get; }
    public WalletAuthorizationService Service { get; }
    public event EventHandler<Wallet?>? WalletChanged { add { } remove { } }
    public WalletFixture(bool locked)
    {
        string path = Path.Combine(directory, "wallet.json");
        Wallet = Neo.Wallets.Wallet.Create("Disposable authorization wallet", path, Password, ProtocolSettings.Default)!;
        Wallet.CreateAccount();
        Wallet.Save();
        if (locked) Wallet = Neo.Wallets.Wallet.Open(path, null, ProtocolSettings.Default)!;
        DataProtectionService.Authenticate = () => throw new InvalidOperationException("Unexpected authentication branch");
        DataProtectionService.Unprotect = () => throw new InvalidOperationException("Unexpected unprotect branch");
        Service = new(new EmptyServices(), new ApplicationDbContext(), this);
    }
    public Wallet? GetWallet() => Wallet;
    public void Dispose() => Directory.Delete(directory, true);
}
