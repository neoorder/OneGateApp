using Neo.Wallets;
using System.Text.Json.Nodes;

namespace NeoOrder.OneGate.Services.RPC;

/// <summary>
/// Adapts verbose Neo node responses to the NEP-21 query types without changing
/// the original response or the atomic units used by transaction fees.
/// </summary>
static class DapiQueryResponseMapper
{
    public static JsonObject MapBlock(JsonObject response, byte addressVersion)
    {
        JsonObject result = (JsonObject)response.DeepClone();
        Rename(result, "previousblockhash", "previousBlockHash");
        Rename(result, "merkleroot", "merkleRoot");
        Rename(result, "nextblockhash", "nextBlockHash");
        Rename(result, "nextconsensus", "nextConsensus");
        result["nextConsensus"] = result["nextConsensus"]!.GetValue<string>().ToScriptHash(addressVersion).ToString();

        foreach (JsonNode? node in result["tx"]!.AsArray())
        {
            JsonObject transaction = node!.AsObject();
            MapTransactionInPlace(transaction, addressVersion);
            // Verbose getblock omits this metadata from its nested transactions.
            transaction["blockHash"] = result["hash"]!.DeepClone();
            transaction["blockTime"] = result["time"]!.DeepClone();
            transaction["confirmations"] = result["confirmations"]!.DeepClone();
        }
        return result;
    }

    public static JsonObject MapTransaction(JsonObject response, byte addressVersion)
    {
        JsonObject result = (JsonObject)response.DeepClone();
        MapTransactionInPlace(result, addressVersion);
        return result;
    }

    static void MapTransactionInPlace(JsonObject transaction, byte addressVersion)
    {
        // Neo RPC serializes these as atomic-unit integer strings, not GAS decimals.
        Rename(transaction, "sysfee", "systemFee");
        Rename(transaction, "netfee", "networkFee");
        Rename(transaction, "validuntilblock", "validUntilBlock");
        Rename(transaction, "blockhash", "blockHash");
        Rename(transaction, "blocktime", "blockTime");
        transaction["sender"] = transaction["sender"]!.GetValue<string>().ToScriptHash(addressVersion).ToString();

        foreach (JsonNode? node in transaction["signers"]!.AsArray())
        {
            JsonObject signer = node!.AsObject();
            Rename(signer, "allowedcontracts", "allowedContracts");
            Rename(signer, "allowedgroups", "allowedGroups");
        }
    }

    static void Rename(JsonObject value, string oldName, string newName)
    {
        if (value.Remove(oldName, out JsonNode? node)) value[newName] = node;
    }
}
