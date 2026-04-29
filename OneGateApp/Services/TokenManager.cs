using Neo;
using Neo.SmartContract.Native;
using Neo.Wallets;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Resources;
using NeoOrder.OneGate.Services.RPC;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;

namespace NeoOrder.OneGate.Services;

public class TokenManager(ApplicationDbContext dbContext, IWalletProvider walletProvider, HttpClient httpClient, RpcClient rpcClient)
{
    public async Task<IReadOnlyList<TokenInfo>> LoadTokensAsync(bool includeHiddens = false)
    {
        List<TokenInfo> tokens = EmbeddedResource.LoadJson<List<TokenInfo>>("tokens.json");
        if (!includeHiddens)
        {
            UInt160[]? hiddens = await dbContext.Settings.GetAsync<UInt160[]>("tokens/hidden");
            if (hiddens != null) tokens.RemoveAll(p => hiddens.Contains(p.Hash));
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
        Ticker[] tickers = await TryLoadTickersAsync(httpClient, $"/api/ticker/price?symbol={token.Symbol}USDT");
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
        if (assets.Length == 0) return assets;
        string query = string.Join('&', assets.Select(p => $"symbol={p.Token.Symbol}USDT"));
        string url = $"/api/ticker/price?{query}";
        Ticker[] tickers = await TryLoadTickersAsync(httpClient, url);
        foreach (Ticker ticker in tickers)
        {
            string? symbol = GetTickerAssetSymbol(ticker.Symbol);
            if (symbol is null) continue;
            AssetInfo? asset = assets.FirstOrDefault(p => p.Token.Symbol == symbol);
            if (asset is not null)
                asset.Token.Price = ticker.Price;
        }
        return assets;
    }

    public async Task<IReadOnlyList<Nep11TokenInfo>> LoadNep11TokensAsync(bool includeHiddens = false)
    {
        List<Nep11TokenInfo> tokens = EmbeddedResource.LoadJson<List<Nep11TokenInfo>>("nft.json");
        if (!includeHiddens)
        {
            UInt160[]? hiddens = await dbContext.Settings.GetAsync<UInt160[]>("tokens/hidden");
            if (hiddens != null) tokens.RemoveAll(p => hiddens.Contains(p.Hash));
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
        UInt160[]? hiddens = await dbContext.Settings.GetAsync<UInt160[]>("tokens/hidden");
        if (hiddens is null) return [];
        var tokens = await LoadTokensAsync(true);
        var nep11tokens = await LoadNep11TokensAsync(true);
        return tokens
            .Where(p => hiddens.Contains(p.Hash))
            .Cast<ITokenInfo>()
            .Concat(nep11tokens.Where(p => hiddens.Contains(p.Hash)))
            .ToArray();
    }

    public async Task<int> GetHiddenTokenCountAsync()
    {
        UInt160[]? hiddens = await dbContext.Settings.GetAsync<UInt160[]>("tokens/hidden");
        return hiddens?.Length ?? 0;
    }

    public async Task HideTokenAsync(UInt160 hash)
    {
        HashSet<UInt160> hiddens = await dbContext.Settings.GetAsync<HashSet<UInt160>>("tokens/hidden") ?? [];
        if (hiddens.Add(hash))
            await dbContext.Settings.PutAsync("tokens/hidden", hiddens);
    }

    public async Task UnhideTokenAsync(UInt160 hash)
    {
        HashSet<UInt160>? hiddens = await dbContext.Settings.GetAsync<HashSet<UInt160>>("tokens/hidden");
        if (hiddens is null) return;
        if (hiddens.Remove(hash))
            await dbContext.Settings.PutAsync("tokens/hidden", hiddens);
    }

    static async Task<Ticker[]> TryLoadTickersAsync(HttpClient httpClient, string url)
    {
        try
        {
            return await httpClient.GetFromJsonAsync<Ticker[]>(url) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException)
        {
            return [];
        }
    }

    static string? GetTickerAssetSymbol(string symbol)
    {
        const string quoteSymbol = "USDT";
        if (symbol.Length <= quoteSymbol.Length || !symbol.EndsWith(quoteSymbol, StringComparison.Ordinal))
            return null;
        return symbol[..^quoteSymbol.Length];
    }
}
