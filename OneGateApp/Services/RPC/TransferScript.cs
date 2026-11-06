using Neo;
using Neo.Extensions;
using Neo.SmartContract;
using Neo.VM;
using System.Numerics;

namespace NeoOrder.OneGate.Services.RPC;

internal static class TransferScript
{
    public static byte[] Nep17(UInt160 assetId, UInt160 from, UInt160 to, BigInteger amount, ContractParameter? data)
    {
        using var builder = new ScriptBuilder();
        return builder.EmitDynamicCall(assetId, "transfer", from, to, amount, data).Emit(OpCode.ASSERT).ToArray();
    }

    public static byte[] Nep11(UInt160 collectionId, byte[] tokenId, UInt160 to, ContractParameter? data)
    {
        using var builder = new ScriptBuilder();
        return builder.EmitDynamicCall(collectionId, "transfer", to, tokenId, data).Emit(OpCode.ASSERT).ToArray();
    }
}
