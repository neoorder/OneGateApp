using Neo;
using Neo.Extensions;
using Neo.Extensions.Factories;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.VM.Types;
using Neo.Wallets;
using NeoOrder.OneGate.Models;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using JsonSerializer = System.Text.Json.JsonSerializer;
using Map = Neo.VM.Types.Map;

namespace NeoOrder.OneGate.Services.RPC;

public class RpcClient(IWalletProvider walletProvider, ProtocolSettings protocolSettings)
{
    readonly HttpClient http = new();

    public async Task<T> RpcSendAsync<T>(string method, params object?[] args) where T : notnull
        => await RpcSendAsync<T>(method, CancellationToken.None, args);

    public async Task<T> RpcSendAsync<T>(string method, CancellationToken cancellationToken, params object?[] args) where T : notnull
    {
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Random.Shared.Next(),
            ["method"] = method,
            ["params"] = new JsonArray(args.Select(p => JsonSerializer.SerializeToNode(p, SharedOptions.JsonSerializerOptions)).ToArray())
        };
        using var requestMsg = new HttpRequestMessage(HttpMethod.Post, SharedOptions.RpcServerUri)
        {
            Content = new StringContent(request.ToJsonString(), Utility.StrictUTF8, "application/json")
        };
        using var responseMsg = await http.SendAsync(requestMsg, cancellationToken);
        responseMsg.EnsureSuccessStatusCode();
        JsonObject response = (await responseMsg.Content.ReadFromJsonAsync<JsonObject>(SharedOptions.JsonSerializerOptions, cancellationToken))!;
        if (response["error"] is JsonObject error)
        {
            int code = error["code"]!.GetValue<int>();
            string message = error["message"]!.GetValue<string>();
            JsonNode? data = error["data"];
            throw new RpcException(code, message, data);
        }
        return JsonSerializer.Deserialize<T>(response["result"], SharedOptions.JsonSerializerOptions)!;
    }

    async Task<InvocationResult> Invoke(byte[] script)
    {
        InvocationResult result = await InvokeScript(script);
        if (result.State != VMState.HALT)
            throw new DapiException(10004, "Invocation failed", result);
        return result;
    }

    async Task<InvocationResult> Invoke(UInt160 scriptHash, string operation, params object?[] args)
    {
        byte[] script;
        using (var builder = new ScriptBuilder())
            script = builder.EmitDynamicCall(scriptHash, operation, args).ToArray();
        return await Invoke(script);
    }

    public async Task<InvocationResult> InvokeScript(byte[] script, Signer[]? signers = null, bool useDiagnostic = false)
    {
        return await RpcSendAsync<InvocationResult>("invokescript", script, signers, useDiagnostic);
    }

    public async Task<ContractState> GetContractState(UInt160 hash)
    {
        return await RpcSendAsync<ContractState>("getcontractstate", hash);
    }

    public async Task<TokenInfo> GetTokenInfo(UInt160 assetId)
    {
        byte[] script;
        using (var builder = new ScriptBuilder())
        {
            builder.EmitDynamicCall(NativeContract.ContractManagement.Hash, "getContract", assetId);
            builder.EmitPush(4);
            builder.Emit(OpCode.PICKITEM);
            builder.EmitPush(0);
            builder.Emit(OpCode.PICKITEM);
            builder.EmitDynamicCall(assetId, "symbol");
            builder.EmitDynamicCall(assetId, "decimals");
            builder.EmitDynamicCall(assetId, "totalSupply");
            script = builder.ToArray();
        }
        InvocationResult result = await Invoke(script);
        return new TokenInfo
        {
            Hash = assetId,
            Name = Utility.StrictUTF8.GetString(result.Stack[0].GetSpan()),
            Symbol = Utility.StrictUTF8.GetString(result.Stack[1].GetSpan()),
            Decimals = (byte)result.Stack[2].GetInteger(),
            TotalSupply = result.Stack[3].GetInteger()
        };
    }

    public async Task<Nep11TokenInfo> GetNep11TokenInfo(UInt160 assetId)
    {
        byte[] script;
        using (var builder = new ScriptBuilder())
        {
            builder.EmitDynamicCall(NativeContract.ContractManagement.Hash, "getContract", assetId);
            builder.EmitPush(4);
            builder.Emit(OpCode.PICKITEM);
            builder.EmitPush(0);
            builder.Emit(OpCode.PICKITEM);
            builder.EmitDynamicCall(assetId, "symbol");
            builder.EmitDynamicCall(assetId, "totalSupply");
            script = builder.ToArray();
        }
        InvocationResult result = await Invoke(script);
        return new Nep11TokenInfo
        {
            Hash = assetId,
            Name = Utility.StrictUTF8.GetString(result.Stack[0].GetSpan()),
            Symbol = Utility.StrictUTF8.GetString(result.Stack[1].GetSpan()),
            TotalSupply = result.Stack[2].GetInteger()
        };
    }

    public async Task<BigInteger> BalanceOf(UInt160 assetId, UInt160 account)
    {
        var result = await Invoke(assetId, "balanceOf", account);
        return result.Stack[0].GetInteger();
    }

    public async Task<BigInteger[]> BalanceOf(UInt160 account, UInt160[] assets)
    {
        byte[] script;
        using (var builder = new ScriptBuilder())
        {
            foreach (var asset in assets)
                builder.EmitDynamicCall(asset, "balanceOf", account);
            script = builder.ToArray();
        }
        InvocationResult result = await Invoke(script);
        return result.Stack.Select(p => p.GetInteger()).ToArray();
    }

    public async Task<UInt160> OwnerOf(UInt160 collectionId, byte[] tokenId)
    {
        InvocationResult result = await Invoke(collectionId, "ownerOf", tokenId);
        return new UInt160(result.Stack[0].GetSpan());
    }

    public async Task<NFT> GetNFTInfo(UInt160 collectionId, byte[] tokenId)
    {
        InvocationResult result = await Invoke(collectionId, "properties", tokenId);
        var map = (Map)result.Stack[0];
        return new NFT
        {
            CollectionId = collectionId,
            TokenId = tokenId,
            Name = map["name"].GetString()!,
            Description = map.TryGetString("description"),
            Image = map.TryGetString("image"),
            TokenURI = map.TryGetString("tokenURI")
        };
    }

    public IAsyncEnumerable<NFT[]> GetNFTPages(UInt160 account, UInt160[] assets, CancellationToken cancellationToken = default)
    {
        return NftPager.ReadAsync(async token =>
            {
                if (assets.Length == 0) return new NftIteratorSession(Guid.Empty, []);
                using var builder = new ScriptBuilder();
                foreach (var asset in assets) builder.EmitDynamicCall(asset, "tokensOf", account);
                JsonObject response = await RpcSendAsync<JsonObject>("invokescript", token, builder.ToArray());
                Guid sessionId = response["session"]!.GetValue<Guid>();
                try
                {
                    if (response["state"]?.GetValue<string>() != nameof(VMState.HALT))
                        throw new DapiException(10004, "NFT enumeration failed");
                    Guid[] iterators = response["stack"]!.AsArray().Select(p => p!["id"]!.GetValue<Guid>()).ToArray();
                    if (iterators.Length != assets.Length) throw new InvalidDataException("NFT iterator response is incomplete.");
                    return new NftIteratorSession(sessionId, iterators);
                }
                catch
                {
                    await CloseNftSessionAsync(sessionId);
                    throw;
                }
            },
            async (session, iterator, count, token) =>
            {
                StackItem[] items = await RpcSendAsync<StackItem[]>("traverseiterator", token, session, iterator, count);
                return items.Select(p => p.GetSpan().ToArray()).ToArray();
            },
            async (collection, ids, token) =>
            {
                using var builder = new ScriptBuilder();
                foreach (var id in ids) builder.EmitDynamicCall(assets[collection], "properties", id);
                InvocationResult result = await RpcSendAsync<InvocationResult>("invokescript", token, builder.ToArray());
                result.EnsureSuccess();
                if (result.Stack.Length != ids.Length) throw new InvalidDataException("NFT metadata response is incomplete.");
                return ids.Zip(result.Stack.Cast<Map>(), (id, map) => new NFT
                {
                    CollectionId = assets[collection], TokenId = id, Name = map["name"].GetString()!,
                    Description = map.TryGetString("description"), Image = map.TryGetString("image"), TokenURI = map.TryGetString("tokenURI")
                }).ToArray();
            }, CloseNftSessionAsync, cancellationToken);
    }

    async Task CloseNftSessionAsync(Guid sessionId)
    {
        if (sessionId == Guid.Empty) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await RpcSendAsync<bool>("terminatesession", timeout.Token, sessionId);
        }
        catch (Exception ex) when (ex is RpcException or HttpRequestException or JsonException or OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to release NFT iterator session: {ex.Message}");
        }
    }

    public async Task<uint> GetBlockCount()
    {
        return await RpcSendAsync<uint>("getblockcount");
    }

    public async Task<UInt256> SendRawTransaction(Transaction transaction)
    {
        JsonObject result = await RpcSendAsync<JsonObject>("sendrawtransaction", transaction.ToArray());
        return result["hash"].Deserialize<UInt256>(SharedOptions.JsonSerializerOptions)!;
    }

    public async Task<Transaction> MakeTransactionAsync(UInt160 assetId, UInt160 from, UInt160 to, BigInteger amount, ContractParameter? data)
    {
        BigInteger balance = await BalanceOf(assetId, from);
        if (balance < amount)
            throw new DapiException(10007, "Insufficient balance");
        var signer = new Signer
        {
            Account = from,
            Scopes = WitnessScope.CalledByEntry
        };
        byte[] script;
        using (var sb = new ScriptBuilder())
            script = sb.EmitDynamicCall(assetId, "transfer", from, to, amount, data).ToArray();
        return await MakeTransactionAsync(script, from, [signer], []);
    }

    public async Task<Transaction> MakeTransactionAsync(UInt160 collectionId, byte[] tokenId, UInt160 to, ContractParameter? data)
    {
        UInt160 from = await OwnerOf(collectionId, tokenId);
        var signer = new Signer
        {
            Account = from,
            Scopes = WitnessScope.CalledByEntry
        };
        byte[] script;
        using (var sb = new ScriptBuilder())
            script = sb.EmitDynamicCall(collectionId, "transfer", to, tokenId, data).ToArray();
        return await MakeTransactionAsync(script, from, [signer], []);
    }

    public async Task<Transaction> MakeTransactionAsync(byte[] script, UInt160? sender = null, Signer[]? signers = null, TransactionAttribute[]? attributes = null, TransactionOptions? options = null)
    {
        signers ??= [];
        attributes ??= [];
        Wallet wallet = walletProvider.GetWallet()!;
        UInt160[] accounts = sender is null
            ? signers.Where(p => wallet.Contains(p.Account)).Select(p => p.Account).ToArray()
            : [sender];
        if (accounts.Length == 0) accounts = wallet.GetAccounts().Select(p => p.ScriptHash).ToArray();
        foreach (var account in accounts)
        {
            Signer[] signersReorder = GetSigners(account, signers);
            var result = await InvokeScript(script, signersReorder);
            if (result.State == VMState.FAULT)
                throw new DapiException(10004, "Script execution failed", result);
            var tx = new Transaction
            {
                Version = 0,
                Nonce = RandomNumberFactory.NextUInt32(),
                Signers = signersReorder,
                Attributes = attributes,
                Script = script,
                Witnesses = signersReorder
                    .Select(p => wallet.GetAccount(p.Account)!)
                    .Select(p => new Witness { InvocationScript = default, VerificationScript = p.Contract!.Script })
                    .ToArray()
            };
            if (options?.SuggestedSystemFee.HasValue == true)
                tx.SystemFee = options.SuggestedSystemFee.Value;
            else if (options?.ExtraSystemFee.HasValue == true)
                tx.SystemFee = result.GasConsumed + options.ExtraSystemFee.Value;
            else
                tx.SystemFee = result.GasConsumed;
            tx.NetworkFee = await CalculateNetworkFee(tx);
            if (options?.ValidUntilBlock.HasValue == true)
                tx.ValidUntilBlock = options.ValidUntilBlock.Value;
            else
                tx.ValidUntilBlock = await GetBlockCount() - 1 + protocolSettings.MaxValidUntilBlockIncrement;
            BigInteger balance = await BalanceOf(NativeContract.GAS.Hash, account);
            if (balance >= tx.SystemFee + tx.NetworkFee) return tx;
        }
        throw new DapiException(10007, "Insufficient GAS");
    }

    static Signer[] GetSigners(UInt160 sender, Signer[] signers)
    {
        for (int i = 0; i < signers.Length; i++)
        {
            if (signers[i].Account.Equals(sender))
            {
                if (i == 0) return signers;
                List<Signer> list = [.. signers];
                list.RemoveAt(i);
                list.Insert(0, signers[i]);
                return [.. list];
            }
        }
        return signers.Prepend(new Signer
        {
            Account = sender,
            Scopes = WitnessScope.None
        }).ToArray();
    }

    public async Task<long> CalculateNetworkFee(Transaction transaction)
    {
        JsonObject result = await RpcSendAsync<JsonObject>("calculatenetworkfee", transaction.ToArray());
        return result["networkfee"].Deserialize<long>(SharedOptions.JsonSerializerOptions)!;
    }
}
