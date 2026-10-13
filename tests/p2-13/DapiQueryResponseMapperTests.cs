using Neo;
using Neo.Network.P2P.Payloads;
using Neo.Wallets;
using NeoOrder.OneGate.Services.RPC;
using System.Text.Json.Nodes;
using Xunit;

namespace OneGate.DapiQuery.Tests;

public class DapiQueryResponseMapperTests
{
    static readonly ProtocolSettings Settings = ProtocolSettings.Default;
    static readonly UInt160 Sender = UInt160.Parse("0x682cca3ebdc66210e5847d7f8115846586079d4a");
    static readonly UInt160 Consensus = UInt160.Parse("0x112233445566778899aabbccddeeff0011223344");
    static readonly UInt256 BlockHash = UInt256.Parse("0x1f4d1defa46faa5e7b9b8d3f79a06bec777d7c26c4aa5f6f5899a291daa87c15");

    [Theory]
    [InlineData(0, 1)]
    [InlineData(100000000, 12345678)]
    [InlineData(9007199254740993, long.MaxValue)]
    public void TransactionConvertsNodeFieldsWithoutScalingOrLosingFeePrecision(long systemFee, long networkFee)
    {
        JsonObject rpc = TransactionResponse(systemFee, networkFee);
        rpc["blockhash"] = BlockHash.ToString();
        rpc["blocktime"] = 1725000000123L;
        rpc["confirmations"] = 19;
        string original = rpc.ToJsonString();

        JsonObject result = DapiQueryResponseMapper.MapTransaction(rpc, Settings.AddressVersion);

        Assert.Equal(systemFee.ToString(System.Globalization.CultureInfo.InvariantCulture), result["systemFee"]!.GetValue<string>());
        Assert.Equal(networkFee.ToString(System.Globalization.CultureInfo.InvariantCulture), result["networkFee"]!.GetValue<string>());
        Assert.Equal(Sender.ToString(), result["sender"]!.GetValue<string>());
        Assert.Equal(123456, result["validUntilBlock"]!.GetValue<int>());
        Assert.Equal(BlockHash.ToString(), result["blockHash"]!.GetValue<string>());
        Assert.Equal(1725000000123L, result["blockTime"]!.GetValue<long>());
        Assert.Equal(19, result["confirmations"]!.GetValue<int>());
        AssertPreserved(rpc, result, "hash", "size", "version", "nonce", "script", "attributes", "witnesses");
        AssertMissing(result, "sysfee", "netfee", "validuntilblock", "blockhash", "blocktime");
        Assert.Equal(original, rpc.ToJsonString());
    }

    [Fact]
    public void TransactionConvertsSignerRestrictionsAndPreservesWitnessRules()
    {
        JsonObject rpc = TransactionResponse();
        JsonObject signer = rpc["signers"]![0]!.AsObject();
        signer["scopes"] = "CustomContracts, CustomGroups, WitnessRules";
        signer["allowedcontracts"] = new JsonArray(Consensus.ToString());
        signer["allowedgroups"] = new JsonArray("03b209fd4f53a7170ea4444e0cb0a6bb6a53c2bd016926989cf85f9b0fba17a70c");
        signer["rules"] = JsonNode.Parse("""[{"action":"Allow","condition":{"type":"CalledByContract","hash":"0x112233445566778899aabbccddeeff0011223344"}}]""");
        string original = rpc.ToJsonString();

        JsonObject result = DapiQueryResponseMapper.MapTransaction(rpc, Settings.AddressVersion);
        JsonObject mappedSigner = result["signers"]![0]!.AsObject();

        Assert.True(JsonNode.DeepEquals(signer["allowedcontracts"], mappedSigner["allowedContracts"]));
        Assert.True(JsonNode.DeepEquals(signer["allowedgroups"], mappedSigner["allowedGroups"]));
        AssertPreserved(signer, mappedSigner, "account", "scopes", "rules");
        AssertMissing(mappedSigner, "allowedcontracts", "allowedgroups");
        Assert.Equal(original, rpc.ToJsonString());
    }

    [Fact]
    public void UnconfirmedTransactionDoesNotInventBlockMetadataOrSignerRestrictions()
    {
        JsonObject result = DapiQueryResponseMapper.MapTransaction(TransactionResponse(), Settings.AddressVersion);

        AssertMissing(result, "blockHash", "blockTime", "confirmations");
        AssertMissing(result["signers"]![0]!.AsObject(), "allowedContracts", "allowedGroups");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BlockMapsHeaderAndEveryTransactionWithParentBlockMetadata(bool hasNextBlock)
    {
        JsonObject rpc = BlockResponse();
        if (hasNextBlock) rpc["nextblockhash"] = BlockHash.ToString();
        rpc["tx"]!.AsArray().Add(TransactionResponse(9007199254740993, 9));
        string original = rpc.ToJsonString();

        JsonObject result = DapiQueryResponseMapper.MapBlock(rpc, Settings.AddressVersion);

        Assert.Equal(rpc["previousblockhash"]!.GetValue<string>(), result["previousBlockHash"]!.GetValue<string>());
        Assert.Equal(rpc["merkleroot"]!.GetValue<string>(), result["merkleRoot"]!.GetValue<string>());
        Assert.Equal(Consensus.ToString(), result["nextConsensus"]!.GetValue<string>());
        AssertPreserved(rpc, result, "hash", "size", "version", "time", "nonce", "index", "primary", "confirmations", "witnesses");
        AssertMissing(result, "previousblockhash", "merkleroot", "nextconsensus", "nextblockhash");
        if (hasNextBlock)
            Assert.Equal(BlockHash.ToString(), result["nextBlockHash"]!.GetValue<string>());
        else
            AssertMissing(result, "nextBlockHash");
        Assert.Equal(2, result["tx"]!.AsArray().Count);
        foreach (JsonNode? node in result["tx"]!.AsArray())
        {
            JsonObject tx = node!.AsObject();
            Assert.Equal(Sender.ToString(), tx["sender"]!.GetValue<string>());
            Assert.True(JsonNode.DeepEquals(result["hash"], tx["blockHash"]));
            Assert.True(JsonNode.DeepEquals(result["time"], tx["blockTime"]));
            Assert.True(JsonNode.DeepEquals(result["confirmations"], tx["confirmations"]));
            AssertMissing(tx, "sysfee", "netfee", "validuntilblock");
        }
        Assert.Equal("9007199254740993", result["tx"]![1]!["systemFee"]!.GetValue<string>());
        Assert.Equal(original, rpc.ToJsonString());
    }

    [Fact]
    public void BlockSupportsAnEmptyTransactionArray()
    {
        JsonObject rpc = BlockResponse();
        rpc["tx"] = new JsonArray();

        JsonObject result = DapiQueryResponseMapper.MapBlock(rpc, Settings.AddressVersion);

        Assert.Empty(result["tx"]!.AsArray());
    }

    [Fact]
    public void ReturnedNestedObjectsAreIndependentOfRpcResult()
    {
        JsonObject rpc = BlockResponse();
        string original = rpc.ToJsonString();

        JsonObject result = DapiQueryResponseMapper.MapBlock(rpc, Settings.AddressVersion);
        result["tx"]![0]!["signers"]![0]!["scopes"] = "Global";
        result["witnesses"]!.AsArray().Clear();

        Assert.Equal(original, rpc.ToJsonString());
    }

    [Fact]
    public void AddressDecodingUsesTheConfiguredAddressVersion()
    {
        const byte otherAddressVersion = 0x17;
        JsonObject rpc = BlockResponse();
        rpc["nextconsensus"] = Consensus.ToAddress(otherAddressVersion);
        rpc["tx"]![0]!["sender"] = Sender.ToAddress(otherAddressVersion);

        JsonObject result = DapiQueryResponseMapper.MapBlock(rpc, otherAddressVersion);

        Assert.Equal(Consensus.ToString(), result["nextConsensus"]!.GetValue<string>());
        Assert.Equal(Sender.ToString(), result["tx"]![0]!["sender"]!.GetValue<string>());
        Assert.Throws<FormatException>(() => DapiQueryResponseMapper.MapBlock(rpc, Settings.AddressVersion));
    }

    [Fact]
    public void InvalidSenderIsRejectedWithoutMutatingRpcResult()
    {
        JsonObject rpc = TransactionResponse();
        rpc["sender"] = "invalid-address";
        string original = rpc.ToJsonString();

        Assert.Throws<FormatException>(() => DapiQueryResponseMapper.MapTransaction(rpc, Settings.AddressVersion));

        Assert.Equal(original, rpc.ToJsonString());
    }

    static Transaction Transaction(long systemFee = 100000000, long networkFee = 12345678) => new()
    {
        Version = 0,
        Nonce = 123,
        SystemFee = systemFee,
        NetworkFee = networkFee,
        ValidUntilBlock = 123456,
        Signers = [new Signer { Account = Sender, Scopes = WitnessScope.CalledByEntry }],
        Attributes = [],
        Script = new byte[] { 0x11 },
        Witnesses = []
    };

    static JsonObject TransactionResponse(long systemFee = 100000000, long networkFee = 12345678) =>
        JsonNode.Parse(Transaction(systemFee, networkFee).ToJson(Settings).ToString())!.AsObject();

    static JsonObject BlockResponse()
    {
        Block block = new()
        {
            Header = new Header
            {
                Version = 0,
                PrevHash = BlockHash,
                MerkleRoot = BlockHash,
                Timestamp = 1725000000123,
                Nonce = ulong.MaxValue,
                Index = 123000,
                PrimaryIndex = 0,
                NextConsensus = Consensus,
                Witness = new Witness { InvocationScript = ReadOnlyMemory<byte>.Empty, VerificationScript = ReadOnlyMemory<byte>.Empty }
            },
            Transactions = [Transaction()]
        };
        JsonObject result = JsonNode.Parse(block.ToJson(Settings).ToString())!.AsObject();
        result["confirmations"] = 19;
        return result;
    }

    static void AssertPreserved(JsonObject rpc, JsonObject result, params string[] names)
    {
        foreach (string name in names)
            Assert.True(JsonNode.DeepEquals(rpc[name], result[name]), $"{name} must be preserved");
    }

    static void AssertMissing(JsonObject result, params string[] names)
    {
        foreach (string name in names) Assert.False(result.ContainsKey(name), $"{name} must not be present");
    }
}
