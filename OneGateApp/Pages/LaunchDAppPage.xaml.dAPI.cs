using CommunityToolkit.Maui.Extensions;
using Neo;
using Neo.Extensions;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.VM;
using Neo.Wallets;
using NeoOrder.OneGate.Controls.Popups;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Models.Diagnostics;
using NeoOrder.OneGate.Models.Intents;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.RPC;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Nodes;

namespace NeoOrder.OneGate.Pages;

[SuppressMessage("CodeQuality", "IDE0051")]
partial class LaunchDAppPage
{
    [RpcMethod]
    async Task<AuthenticationResponsePayload> Authenticate(AuthenticationChallengePayload payload)
    {
        try
        {
            payload.Validate(protocolSettings);
        }
        catch (NotSupportedException ex)
        {
            throw new DapiException(10001, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new DapiException(10002, ex.Message);
        }
        if (payload.Domain != new Uri(DApp.Url).Host)
            throw new DapiException(10002, "Domain mismatch");
        if (!await walletAuthorizationService.RequestAuthorizationAsync(this, Strings.LoginRequest, Strings.LoginRequestText))
            throw new DapiException(10006, "Operation cancelled");
        WalletAccount account = walletProvider.GetWallet()!.GetDefaultAccount()!;
        return payload.CreateResponse(account, protocolSettings);
    }

    [RpcMethod]
    Account[] GetAccounts()
    {
        return walletProvider.GetWallet()!.GetAccounts().Select(Account.From).ToArray();
    }

    [RpcMethod]
    async Task<string> PickAddress(string? prompt)
    {
        var popup = serviceProvider.GetServiceOrCreateInstance<PickAddressPopup>();
        if (!string.IsNullOrEmpty(prompt)) popup.Message = prompt;
        var result = await this.ShowPopupAsync<string>(popup);
        return result.Result ?? throw new OperationCanceledException();
    }

    [RpcMethod]
    async Task<BigInteger> GetBalance(UInt160 asset, UInt160 account)
    {
        return await rpcClient.BalanceOf(asset, account);
    }

    [RpcMethod]
    async Task<UInt256> Send(UInt160 asset, UInt160? from, UInt160 to, BigInteger amount, ContractParameter? data)
    {
        Wallet wallet = walletProvider.GetWallet()!;
        WalletAccount account = from is null ? wallet.GetDefaultAccount()! : wallet.GetAccount(from)
            ?? throw new DapiException(10003, "Account not found");
        Transaction tx = await rpcClient.MakeTransactionAsync(asset, account.ScriptHash, to, amount, data);
        TransactionIntent[] intents = [new TransferIntent
        {
            Asset = await rpcClient.GetTokenInfo(asset),
            From = account.ScriptHash,
            To = to,
            Amount = amount,
            Data = data
        }];
        return await SignAndSendAsync(tx, intents);
    }

    [RpcMethod]
    async Task<InvocationResult> Call(InvocationArguments invocation)
    {
        byte[] script;
        using (var builder = new ScriptBuilder())
        {
            invocation.EmitScript(builder);
            script = builder.ToArray();
        }
        return await rpcClient.InvokeScript(script);
    }

    [RpcMethod]
    async Task<UInt256> Invoke(InvocationArguments[] invocations, Signer[]? signers, TransactionAttribute[]? attributes, TransactionOptions? options)
    {
        if (options?.SuggestedSystemFee <= 0 || options?.ExtraSystemFee < 0)
            throw new DapiException(10002, "Invalid fee");
        byte[] script;
        using (var builder = new ScriptBuilder())
        {
            foreach (var invocation in invocations)
                invocation.EmitScript(builder);
            script = builder.ToArray();
        }
        Transaction tx = await rpcClient.MakeTransactionAsync(script, signers: signers, attributes: attributes, options: options);
        List<TransactionIntent> intents = [];
        foreach (var invocation in invocations)
        {
            intents.Add(new InvocationIntent
            {
                Contract = await rpcClient.GetContractState(invocation.Hash),
                Method = invocation.Operation,
                Arguments = invocation.Arguments
            });
        }
        return await SignAndSendAsync(tx, intents.ToArray());
    }

    [RpcMethod]
    async Task<ContractParametersContext> MakeTransaction(InvocationArguments[] invocations, Signer[]? signers, TransactionAttribute[]? attributes, TransactionOptions? options)
    {
        if (options?.SuggestedSystemFee <= 0 || options?.ExtraSystemFee < 0)
            throw new DapiException(10002, "Invalid fee");
        byte[] script;
        using (var builder = new ScriptBuilder())
        {
            foreach (var invocation in invocations)
                invocation.EmitScript(builder);
            script = builder.ToArray();
        }
        Transaction tx = await rpcClient.MakeTransactionAsync(script, signers: signers, attributes: attributes, options: options);
        return new ContractParametersContext(null!, tx, protocolSettings.Network);
    }

    [RpcMethod]
    async Task<ContractParametersContext> Sign(ContractParametersContext context)
    {
        if (context.Verifiable is not Transaction tx)
            throw new DapiException(10001, "Only transaction signing is supported");
        InvocationResult result = await rpcClient.InvokeScript(tx.Script.ToArray(), tx.Signers, true);
        Invocation[] invocations = result.Diagnostics!.Traces.Calls.OfType<Invocation>().ToArray();
        List<TransactionIntent> intents = new();
        foreach (var invocation in invocations)
        {
            TransactionIntent intent = await invocation.ToIntentAsync(rpcClient);
            TransactionIntent? specific = await intent.TryConvertToMoreSpecificIntentAsync(rpcClient);
            if (specific is not null) intent = specific;
            intents.Add(intent);
        }
        var popup = serviceProvider.GetServiceOrCreateInstance<SendTransactionPopup>();
        popup.Title = Strings.SignTransaction;
        popup.Message = Strings.SignTransactionText;
        popup.Transaction = tx;
        popup.Intents = intents.ToArray();
        popup.InvocationResult = result;
        var popup_result = await this.ShowPopupAsync<bool>(popup);
        if (!popup_result.Result) throw new OperationCanceledException();
        if (!walletProvider.GetWallet()!.SignWithWorkaround(context))
            throw new DapiException(10000, "Failed to sign transaction");
        return context;
    }

    [RpcMethod]
    async Task<SignedMessage> SignMessage(string message, UInt160? account, SignOptions? options)
    {
        if (options?.IsTypedData == true)
            throw new DapiException(10001, "Typed data signing is not supported");
        if (options?.IsLedgerCompatible == true)
            throw new DapiException(10001, "Ledger compatible signing is not supported");
        var popup = serviceProvider.GetServiceOrCreateInstance<SignMessagePopup>();
        popup.Account = account?.ToAddress(protocolSettings.AddressVersion);
        popup.Message = message;
        var result = await this.ShowPopupAsync<string?>(popup);
        if (result.Result is null) throw new OperationCanceledException();
        byte[] payload = options?.IsBase64Encoded == true
            ? Convert.FromBase64String(message)
            : Utility.StrictUTF8.GetBytes(message);
        account ??= result.Result.ToScriptHash(protocolSettings.AddressVersion);
        KeyPair key = walletProvider.GetWallet()!.GetAccount(account)!.GetKey()!;
        return new SignedMessage
        {
            Payload = payload,
            Signature = Workarounds.Sign(payload, key),
            Account = account,
            PublicKey = key.PublicKey
        };
    }

    [RpcMethod]
    async Task<UInt256> Relay(ContractParametersContext context)
    {
        if (!context.Completed)
            throw new DapiException(10002, "Context is not fully signed");
        if (context.Verifiable is not Transaction tx)
            throw new DapiException(10001, "Only transaction relaying is supported");
        tx.Witnesses = context.GetWitnesses();
        return await rpcClient.SendRawTransaction(tx);
    }

    [RpcMethod]
    async Task<JsonObject> GetBlock(JsonValue hashOrIndex)
    {
        return await rpcClient.RpcSendAsync<JsonObject>("getblock", hashOrIndex, true);
    }

    [RpcMethod]
    async Task<uint> GetBlockCount()
    {
        return await rpcClient.GetBlockCount();
    }

    [RpcMethod]
    async Task<JsonObject> GetTransaction(UInt256 txid)
    {
        return await rpcClient.RpcSendAsync<JsonObject>("getrawtransaction", txid, true);
    }

    [RpcMethod]
    async Task<JsonObject> GetApplicationLog(UInt256 txid)
    {
        return await rpcClient.RpcSendAsync<JsonObject>("getapplicationlog", txid);
    }

    [RpcMethod]
    async Task<byte[]> GetStorage(UInt160 hash, byte[] key)
    {
        return await rpcClient.RpcSendAsync<byte[]>("getstorage", hash, key);
    }

    [RpcMethod]
    async Task<TokenInfo> GetTokenInfo(UInt160 hash)
    {
        return await rpcClient.GetTokenInfo(hash);
    }

    async Task<UInt256> SignAndSendAsync(Transaction tx, TransactionIntent[]? intents)
    {
        var popup = serviceProvider.GetServiceOrCreateInstance<SendTransactionPopup>();
        popup.Transaction = tx;
        if (intents != null)
        {
            for (int i = 0; i < intents.Length; i++)
            {
                TransactionIntent? specific = await intents[i].TryConvertToMoreSpecificIntentAsync(rpcClient);
                if (specific != null) intents[i] = specific;
            }
        }
        popup.Intents = intents;
        var result = await this.ShowPopupAsync<bool>(popup);
        if (!result.Result) throw new OperationCanceledException();
        var context = new ContractParametersContext(null!, tx, protocolSettings.Network);
        if (!walletProvider.GetWallet()!.SignWithWorkaround(context))
            throw new DapiException(10000, "Failed to sign transaction");
        if (!context.Completed)
            throw new DapiException(10001, "Multisignature transaction requires more signatures");
        tx.Witnesses = context.GetWitnesses();
        return await rpcClient.SendRawTransaction(tx);
    }
}
