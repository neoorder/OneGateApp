using Meshline.Identity;
using Meshline.Interactions;
using Neo.Cryptography;
using Neo.SmartContract;
using Neo.Wallets;
using System.Collections.Immutable;
using System.Globalization;

namespace NeoOrder.OneGate.Services.Messaging;

/// <summary>Signs Meshline input with an existing wallet account.</summary>
/// <remarks>The caller must authorize the operation before requesting a signature.</remarks>
sealed class NeoAccountSigner : IAccountSigner
{
    readonly WalletAccount account;

    public string AccountId { get; }
    public ImmutableArray<byte> PublicKey { get; }

    public NeoAccountSigner(WalletAccount account, uint network)
    {
        ArgumentNullException.ThrowIfNull(account);
        this.account = account;
        PublicKey = ReadPublicKey(account);
        string chainId = $"neo:{network.ToString(CultureInfo.InvariantCulture)}";
        AccountId = AccountAdapter.GetAccountId(chainId, PublicKey.AsSpan());
        if (AccountId != $"{chainId}:{account.Address}")
            throw new InvalidOperationException("The wallet address does not match its signing contract.");
    }

    public Task<byte[]> SignAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateAccount();

        KeyPair key = account.GetKey()
            ?? throw new InvalidOperationException("The wallet account has no signing key.");
        if (!PublicKey.AsSpan().SequenceEqual(key.PublicKey.EncodePoint(true)))
            throw new InvalidOperationException("The signing key does not match the Meshline account.");

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Crypto.Sign(data.ToArray(), key, HashAlgorithm.SHA256));
    }

    void ValidateAccount()
    {
        if (!PublicKey.AsSpan().SequenceEqual(ReadPublicKey(account).AsSpan()))
            throw new InvalidOperationException("The wallet signing contract has changed.");
    }

    static ImmutableArray<byte> ReadPublicKey(WalletAccount account)
    {
        Contract? contract = account.Contract;
        if (account.Lock || !account.HasKey || contract is null
            || contract.ParameterList.Length != 1
            || contract.ParameterList[0] != ContractParameterType.Signature
            || !Neo.SmartContract.Helper.IsSignatureContract(contract.Script))
            throw new InvalidOperationException("Meshline requires an unlocked Neo N3 single-signature account with a private key.");

        // A standard Neo N3 signature contract embeds its compressed public key after PUSHDATA1(33).
        return [.. contract.Script.AsSpan(2, 33)];
    }
}
