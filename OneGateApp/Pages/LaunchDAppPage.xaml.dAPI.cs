using CommunityToolkit.Maui.Extensions;
using Neo;
using Neo.Cryptography;
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
using NeoOrder.OneGate.Services.RemoteDebug;
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
        if (!payload.Domain.Equals(new Uri(DApp.Url).Host, StringComparison.OrdinalIgnoreCase))
            throw new DapiException(10002, "Domain mismatch");
        if (IsRemoteDebugSession)
            await RequestRemoteApprovalAsync("authenticate", payload);
        else if (!await walletAuthorizationService.RequestAuthorizationAsync(this, Strings.LoginRequest, Strings.LoginRequestText))
            throw new DapiException(10006, "Operation cancelled");
        await activityLogService.RecordWalletAuthorizationAsync(DApp);
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
        if (IsRemoteDebugSession)
        {
            RemoteDebugApprovalResult approval = await RequestRemoteApprovalAsync("pickAddress", prompt);
            if (approval.HasResult)
            {
                if (approval.Result is not JsonValue value
                    || !value.TryGetValue(out string? address)
                    || string.IsNullOrWhiteSpace(address))
                    throw new DapiException(10002, "The remote debugger returned an invalid address.");
                try
                {
                    return address.Trim().ToScriptHash(protocolSettings.AddressVersion).ToAddress(protocolSettings.AddressVersion);
                }
                catch (FormatException)
                {
                    throw new DapiException(10002, "The remote debugger returned an invalid address.");
                }
            }
            return walletProvider.GetWallet()!.GetDefaultAccount()!.ScriptHash.ToAddress(protocolSettings.AddressVersion);
        }
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
        if (IsRemoteDebugSession)
            await RequestRemoteApprovalAsync("send", asset, from, to, amount, data);
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
        if (IsRemoteDebugSession)
            await RequestRemoteApprovalAsync("invoke", invocations, signers, attributes, options);
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
        if (IsRemoteDebugSession)
        {
            await RequestRemoteApprovalAsync("sign", context);
        }
        else
        {
            var popup = serviceProvider.GetServiceOrCreateInstance<SendTransactionPopup>();
            popup.Title = Strings.SignTransaction;
            popup.Message = Strings.SignTransactionText;
            popup.Transaction = tx;
            popup.Intents = intents.ToArray();
            popup.InvocationResult = result;
            var popup_result = await this.ShowPopupAsync<bool>(popup);
            if (!popup_result.Result) throw new OperationCanceledException();
        }
        if (!walletProvider.GetWallet()!.Sign(context))
            throw new DapiException(10000, "Failed to sign transaction");
        await activityLogService.RecordSignatureAsync(DApp);
        return context;
    }

    [RpcMethod]
    async Task<SignedMessage> SignMessage(string message, UInt160? account, SignOptions? options)
    {
        if (options?.IsTypedData == true)
            throw new DapiException(10001, "Typed data signing is not supported");
        if (options?.IsLedgerCompatible == true)
            throw new DapiException(10001, "Ledger compatible signing is not supported");
        if (IsRemoteDebugSession)
        {
            await RequestRemoteApprovalAsync("signMessage", message, account, options);
            account ??= walletProvider.GetWallet()!.GetDefaultAccount()!.ScriptHash;
        }
        else
        {
            var popup = serviceProvider.GetServiceOrCreateInstance<SignMessagePopup>();
            popup.Account = account?.ToAddress(protocolSettings.AddressVersion);
            popup.IsBase64Encoded = options?.IsBase64Encoded == true;
            popup.Message = message;
            var result = await this.ShowPopupAsync<string?>(popup);
            if (result.Result is null) throw new OperationCanceledException();
            account ??= result.Result.ToScriptHash(protocolSettings.AddressVersion);
        }
        byte[] payload = options?.IsBase64Encoded == true
            ? Convert.FromBase64String(message)
            : Utility.StrictUTF8.GetBytes(message);
        KeyPair key = walletProvider.GetWallet()!.GetAccount(account)!.GetKey()!;
        SignedMessage signedMessage = new()
        {
            Payload = payload,
            Signature = Crypto.Sign(payload, key),
            Account = account,
            PublicKey = key.PublicKey
        };
        await activityLogService.RecordSignatureAsync(DApp);
        return signedMessage;
    }

    [RpcMethod]
    async Task<UInt256> Relay(ContractParametersContext context)
    {
        if (!context.Completed)
            throw new DapiException(10002, "Context is not fully signed");
        if (context.Verifiable is not Transaction tx)
            throw new DapiException(10001, "Only transaction relaying is supported");
        if (IsRemoteDebugSession)
            await RequestRemoteApprovalAsync("relay", context);
        tx.Witnesses = context.GetWitnesses();
        UInt256 transactionHash = await rpcClient.SendRawTransaction(tx);
        await activityLogService.RecordTransactionAsync(DApp, transactionHash);
        return transactionHash;
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

    async Task<RemoteDebugApprovalResult> RequestRemoteApprovalAsync(string method, params object?[] parameters)
    {
        if (remoteDebugSessionId is null || remoteDebugService is null)
            throw new InvalidOperationException("A remote debug session is required for remote approval.");
        JsonArray serializedParameters = new(parameters.Select(parameter => parameter is null
            ? null
            : System.Text.Json.JsonSerializer.SerializeToNode(parameter, parameter.GetType(), SharedOptions.JsonSerializerOptions)).ToArray());
        RemoteDebugApprovalResult approval;
        try
        {
            approval = await remoteDebugService.RequestDapiApprovalAsync(remoteDebugSessionId, method, serializedParameters);
        }
        catch
        {
            throw new OperationCanceledException();
        }
        if (!approval.Approved) throw new OperationCanceledException();
        return approval;
    }

    async Task<UInt256> SignAndSendAsync(Transaction tx, TransactionIntent[]? intents)
    {
        if (intents != null)
        {
            for (int i = 0; i < intents.Length; i++)
            {
                TransactionIntent? specific = await intents[i].TryConvertToMoreSpecificIntentAsync(rpcClient);
                if (specific != null) intents[i] = specific;
            }
        }
        if (!IsRemoteDebugSession)
        {
            var popup = serviceProvider.GetServiceOrCreateInstance<SendTransactionPopup>();
            popup.Transaction = tx;
            popup.Intents = intents;
            var result = await this.ShowPopupAsync<bool>(popup);
            if (!result.Result) throw new OperationCanceledException();
        }
        var context = new ContractParametersContext(null!, tx, protocolSettings.Network);
        if (!walletProvider.GetWallet()!.Sign(context))
            throw new DapiException(10000, "Failed to sign transaction");
        if (!context.Completed)
            throw new DapiException(10001, "Multisignature transaction requires more signatures");
        tx.Witnesses = context.GetWitnesses();
        UInt256 transactionHash = await rpcClient.SendRawTransaction(tx);
        await activityLogService.RecordTransactionAsync(DApp, transactionHash);
        return transactionHash;
    }
}
