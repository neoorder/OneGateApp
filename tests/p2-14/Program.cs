using Neo;
using Neo.Cryptography.ECC;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.Wallets;
using Neo.Wallets.NEP6;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Services.RPC;
using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;

int failures = 0;
await Run("external cosigner fails before RPC", async () =>
{
    var wallet = CreateWallet();
    var local = AddContract(wallet);
    await Reject(wallet, null, [SignerFor(local.ScriptHash), SignerFor(Hash(1))], 10001);
});
await Run("external-only signer fails before RPC", async () =>
{
    var wallet = CreateWallet();
    AddContract(wallet);
    await Reject(wallet, null, [SignerFor(Hash(1))], 10001);
});
await Run("deployed contract without local verification metadata is unsupported", async () =>
{
    var wallet = CreateWallet();
    var local = AddContract(wallet);
    await Reject(wallet, null, [SignerFor(local.ScriptHash), SignerFor(Neo.SmartContract.Native.NativeContract.GAS.Hash)], 10001);
});
await Run("watch-only signer fails before RPC", async () =>
{
    var wallet = CreateWallet();
    AddContract(wallet);
    var watch = wallet.CreateAccount(Hash(2));
    await Reject(wallet, null, [SignerFor(watch.ScriptHash)], 10001);
});
await Run("unknown explicit sender fails before RPC", async () =>
{
    var wallet = CreateWallet();
    AddContract(wallet);
    await Reject(wallet, Hash(3), [], 10001);
});
await Run("watch-only explicit sender fails before RPC", async () =>
{
    var wallet = CreateWallet();
    var watch = wallet.CreateAccount(Hash(2));
    await Reject(wallet, watch.ScriptHash, [], 10001);
});
await Run("empty wallet has explicit missing-account response", async () =>
{
    var wallet = CreateWallet();
    await Reject(wallet, null, [], 10003);
});
await Run("wallet with only watch-only accounts has no usable implicit payer", async () =>
{
    var wallet = CreateWallet();
    wallet.CreateAccount(Hash(2));
    await Reject(wallet, null, [], 10003);
});
await Run("missing wallet fails before RPC", () => Reject(null, null, [], 10003));
await Run("known local verification contract needs no private key for construction", async () =>
{
    var wallet = CreateWallet();
    var local = AddContract(wallet);
    Assert(!local.HasKey && !local.WatchOnly, "Fixture should have verification script but no private key");
    using var rpc = new FakeRpc();
    var tx = await Client(wallet, rpc).MakeTransactionAsync([0x11], signers: [SignerFor(local.ScriptHash)]);
    Assert(tx.Sender == local.ScriptHash, "Local payer changed");
    Assert(tx.Witnesses[0].VerificationScript.Span.SequenceEqual(local.Contract!.Script), "Verification script changed");
    Assert(tx.SystemFee == 1 && tx.NetworkFee == 2, "Fee handling changed");
    Assert(rpc.Methods.SequenceEqual(new[] { "invokescript", "calculatenetworkfee", "getblockcount", "invokescript" }), "Normal RPC path changed");
});
await Run("implicit payer selection skips watch-only entries", async () =>
{
    var wallet = CreateWallet();
    wallet.CreateAccount(Hash(2));
    var local = AddContract(wallet);
    using var rpc = new FakeRpc();
    var tx = await Client(wallet, rpc).MakeTransactionAsync([0x11]);
    Assert(tx.Sender == local.ScriptHash, "Incorrect implicit payer");
});
await Run("local multisig verification contract remains supported", async () =>
{
    var wallet = CreateWallet();
    var contract = Contract.CreateMultiSigContract(1, [ECCurve.Secp256r1.G]);
    var local = wallet.CreateAccount(contract, (KeyPair?)null);
    using var rpc = new FakeRpc();
    var tx = await Client(wallet, rpc).MakeTransactionAsync([0x11], signers: [SignerFor(local.ScriptHash)]);
    Assert(tx.Witnesses[0].VerificationScript.Span.SequenceEqual(contract.Script), "Multisig verification script changed");
});
await Run("multiple local signers preserve scopes and sender order", async () =>
{
    var wallet = CreateWallet();
    var first = AddContract(wallet);
    var second = wallet.CreateAccount(Contract.CreateMultiSigContract(1, [ECCurve.Secp256r1.G]), (KeyPair?)null);
    var firstSigner = new Signer { Account = first.ScriptHash, Scopes = WitnessScope.Global };
    var secondSigner = SignerFor(second.ScriptHash);
    using var rpc = new FakeRpc();
    var tx = await Client(wallet, rpc).MakeTransactionAsync([0x11], second.ScriptHash, [firstSigner, secondSigner]);
    Assert(tx.Signers.SequenceEqual(new[] { secondSigner, firstSigner }), "Signer scope or order changed");
    Assert(tx.Witnesses[0].VerificationScript.Span.SequenceEqual(second.Contract!.Script), "Payer witness order changed");
    Assert(tx.Witnesses[1].VerificationScript.Span.SequenceEqual(first.Contract!.Script), "Cosigner witness order changed");
});
Console.WriteLine($"{13 - failures}/13 passed");
return failures == 0 ? 0 : 1;

async Task Run(string name, Func<Task> test)
{
    try { await test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failures++; Console.Error.WriteLine($"FAIL {name}: {e.GetType().Name}: {e.Message}"); }
}

static async Task Reject(Wallet? wallet, UInt160? sender, Signer[] signers, int expectedCode)
{
    using var rpc = new FakeRpc();
    try
    {
        await Client(wallet, rpc).MakeTransactionAsync([0x11], sender, signers);
        throw new Exception("Expected a dAPI error");
    }
    catch (DapiException e)
    {
        Assert(e.Code == expectedCode, $"Expected dAPI {expectedCode}, got {e.Code}");
        Assert(rpc.Methods.Count == 0, "Unsupported request reached RPC");
    }
}

static NEP6Wallet CreateWallet() => new(Path.Combine(Path.GetTempPath(), $"onegate-p2-14-{Guid.NewGuid():N}.json"), "test-only-not-a-real-wallet", ProtocolSettings.Default, "test-only");
static WalletAccount AddContract(Wallet wallet) => wallet.CreateAccount(Contract.CreateSignatureContract(ECCurve.Secp256r1.G), (KeyPair?)null);
static UInt160 Hash(byte first) => new([first, .. new byte[19]]);
static Signer SignerFor(UInt160 hash) => new() { Account = hash, Scopes = WitnessScope.CalledByEntry };
static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
static RpcClient Client(Wallet? wallet, FakeRpc rpc)
{
    var client = new RpcClient(new WalletProvider(wallet), ProtocolSettings.Default);
    // Test-only replacement of the private transport: no real node requests, signatures or wallet saves.
    var transport = typeof(RpcClient).GetField("http", BindingFlags.Instance | BindingFlags.NonPublic)!;
    ((HttpClient)transport.GetValue(client)!).Dispose();
    transport.SetValue(client, new HttpClient(rpc));
    return client;
}

sealed class WalletProvider(Wallet? wallet) : IWalletProvider
{
    public event EventHandler<Wallet?>? WalletChanged { add { } remove { } }
    public Wallet? GetWallet() => wallet;
}

sealed class FakeRpc : HttpMessageHandler
{
    public List<string> Methods { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
        string method = body["method"]!.GetValue<string>();
        Methods.Add(method);
        string result = method switch
        {
            "invokescript" => """{"script":"","state":"HALT","gasconsumed":"1","notifications":[],"stack":[{"type":"Integer","value":"1000000000"}]}""",
            "calculatenetworkfee" => """{"networkfee":"2"}""",
            "getblockcount" => "100",
            _ => throw new Exception($"Unexpected RPC method: {method}")
        };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{result}}}") };
    }
}
