using Neo;
using System.Numerics;

namespace NeoOrder.OneGate.Models;

record DAppAsset
{
    public required UInt160 Hash { get; init; }
    public required string Name { get; init; }
    public required string Symbol { get; init; }
    public required byte Decimals { get; init; }
    public required BigInteger Balance { get; init; }
    public string Source { get; init; } = "walletAssets";

    public static DAppAsset From(AssetInfo asset)
    {
        return new DAppAsset
        {
            Hash = asset.Token.Hash,
            Name = asset.Token.Name,
            Symbol = asset.Token.Symbol,
            Decimals = asset.Token.Decimals,
            Balance = asset.Balance
        };
    }
}
