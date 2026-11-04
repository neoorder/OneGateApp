using Microsoft.EntityFrameworkCore;
using Neo;
using Neo.SmartContract.Native;
using Neo.Wallets;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Resources;
using NeoOrder.OneGate.Services.RPC;
using System.Net.Http.Json;
using System.Numerics;

namespace NeoOrder.OneGate.Services;

public class TokenManager(IServiceScopeFactory scopeFactory, IWalletProvider walletProvider, HttpClient httpClient, RpcClient rpcClient)
{
    public async Task<IReadOnlyList<TokenInfo>> LoadTokensAsync(bool includeHiddens = false)
    {
        List<TokenInfo> tokens = EmbeddedResource.LoadJson<List<TokenInfo>>("tokens.json");
        if (!includeHiddens)
        {
            var hiddens = await GetHiddenTokenHashesAsync();
            tokens.RemoveAll(p => hiddens.Contains(p.Hash.ToString()));
        }
        return tokens;
    }

    public async Task<AssetInfo> LoadAssetAsync(UInt160 assetId)
    {
        Wallet wallet = walletProvider.GetWallet()!;
        WalletAccount account = wallet.GetDefaultAccount()!;
        var tokens = await LoadTokensAsync(true);
        TokenInfo token = tokens.FirstOrDefault(p => p.Hash == assetId) ?? await rpcClient.GetTokenInfo(assetId);
        BigInteger balance = await rpcClient.BalanceOf(assetId, account.ScriptHash);
        Ticker[] tickers = (await httpClient.GetFromJsonAsync<Ticker[]>($"/api/ticker/price?symbol={token.Symbol}USDT"))!;
        token.Price = tickers.FirstOrDefault()?.Price;
        return new AssetInfo
        {
            Token = token,
            Balance = balance
        };
    }

    public async Task<IReadOnlyList<AssetInfo>> LoadAssetsAsync(bool includeHiddens = false)
    {
        Wallet wallet = walletProvider.GetWallet()!;
        WalletAccount account = wallet.GetDefaultAccount()!;
        var tokens = await LoadTokensAsync(includeHiddens);
        BigInteger[] balance = await rpcClient.BalanceOf(account.ScriptHash, tokens.Select(p => p.Hash).ToArray());
        AssetInfo[] assets = tokens
            .Zip(balance, (x, y) => new AssetInfo { Token = x, Balance = y })
            .Where(p => p.Balance > 0 || p.Token.Hash == NativeContract.NEO.Hash || p.Token.Hash == NativeContract.GAS.Hash)
            .ToArray();
        string query = string.Join('&', assets.Select(p => $"symbol={p.Token.Symbol}USDT"));
        string url = $"/api/ticker/price?{query}";
        Ticker[] tickers = (await httpClient.GetFromJsonAsync<Ticker[]>(url))!;
        foreach (Ticker ticker in tickers)
        {
            var asset = assets.First(p => p.Token.Symbol == ticker.Symbol[0..^4]);
            asset.Token.Price = ticker.Price;
        }
        return assets;
    }

    public async Task<IReadOnlyList<Nep11TokenInfo>> LoadNep11TokensAsync(bool includeHiddens = false)
    {
        List<Nep11TokenInfo> tokens = EmbeddedResource.LoadJson<List<Nep11TokenInfo>>("nft.json");
        if (!includeHiddens)
        {
            var hiddens = await GetHiddenTokenHashesAsync();
            tokens.RemoveAll(p => hiddens.Contains(p.Hash.ToString()));
        }
        return tokens;
    }

    public async Task<IReadOnlyList<NFT>> LoadNFTsAsync(bool includeHiddens = false)
    {
        Wallet wallet = walletProvider.GetWallet()!;
        WalletAccount account = wallet.GetDefaultAccount()!;
        var tokens = await LoadNep11TokensAsync(includeHiddens);
        NFT[] nfts = await rpcClient.GetNFTs(account.ScriptHash, tokens.Select(p => p.Hash).ToArray());
        foreach (NFT nft in nfts)
            nft.TokenInfo = tokens.First(p => p.Hash == nft.CollectionId);
        return nfts;
    }

    public async Task<IReadOnlyList<ITokenInfo>> LoadHiddenTokens()
    {
        var hiddens = await GetHiddenTokenHashesAsync();
        if (hiddens.Count == 0) return [];
        var tokens = await LoadTokensAsync(true);
        var nep11tokens = await LoadNep11TokensAsync(true);
        return tokens
            .Where(p => hiddens.Contains(p.Hash.ToString()))
            .Cast<ITokenInfo>()
            .Concat(nep11tokens.Where(p => hiddens.Contains(p.Hash.ToString())))
            .ToArray();
    }

    public async Task<int> GetHiddenTokenCountAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await database.HiddenTokens.CountAsync();
    }

    public async Task HideTokenAsync(UInt160 hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        string value = hash.ToString();
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await database.Database.ExecuteSqlAsync($"""
            INSERT INTO [HiddenTokens]([Hash]) VALUES({value})
            ON CONFLICT([Hash]) DO NOTHING
            """);
    }

    public async Task UnhideTokenAsync(UInt160 hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        string value = hash.ToString();
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await database.HiddenTokens.Where(p => p.Hash == value).ExecuteDeleteAsync();
    }

    async Task<HashSet<string>> GetHiddenTokenHashesAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        string[] hashes = await database.HiddenTokens.Select(p => p.Hash).ToArrayAsync();
        return hashes.ToHashSet(StringComparer.Ordinal);
    }
}
