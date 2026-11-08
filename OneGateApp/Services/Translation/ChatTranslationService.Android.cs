#if ANDROID
using Android.Gms.Extensions;
using Android.Runtime;
using Xamarin.Google.MLKit.Common.Model;
using Xamarin.Google.MLKit.NL.LanguageId;
using Xamarin.Google.MLKit.NL.Translate;
using NativeTranslation = Xamarin.Google.MLKit.NL.Translate.Translation;

namespace NeoOrder.OneGate.Services.Translation;

public sealed class ChatTranslationService : IChatTranslationService
{
    readonly TranslationModelDownloads modelDownloads = new();

    public bool IsAvailable => true;
    public bool CanManageModels => true;
    public event EventHandler? ModelDownloaded;

    public Task<IReadOnlyList<string>> GetLanguagesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(TranslateLanguage.AllLanguages.Select(TranslationLanguages.Normalize).ToArray());
    }

    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TranslationLanguages.HasText(request.Text)) throw new ChatTranslationException(TranslationFailure.NoText);
            var languages = await GetLanguagesAsync(cancellationToken);
            string target = TranslationLanguages.Match(request.TargetLanguage, languages)
                ?? throw new ChatTranslationException(TranslationFailure.UnsupportedTargetLanguage);
            string? sourceTag = request.SourceLanguage;
            if (sourceTag is null)
            {
                using var identificationOptions = new LanguageIdentificationOptions.Builder().Build();
                using var identifier = LanguageIdentification.GetClient(identificationOptions);
                try
                {
                    using var detected = await identifier.IdentifyLanguage(request.Text).AsAsync<Java.Lang.String>();
                    cancellationToken.ThrowIfCancellationRequested();
                    sourceTag = detected?.ToString();
                }
                finally { identifier.Close(); }
            }
            var (source, sourceModel) = TranslationLanguages.ResolveMlKitSource(sourceTag, languages);

            // ML Kit requires no download for a same-model request.
            if (sourceModel != target)
            {
                var downloaded = await GetDownloadedModelsAsync(cancellationToken);
                // English is the pivot language built into every language-specific model.
                string[] missing = new[] { sourceModel, target }.Where(language => language != "en" && !downloaded.Contains(language)).ToArray();
                if (missing.Length > 0) throw new ChatTranslationException(TranslationFailure.ModelsRequired, missing);
            }
            cancellationToken.ThrowIfCancellationRequested();
            using var options = new TranslatorOptions.Builder().SetSourceLanguage(NativeLanguage(sourceModel))!
                .SetTargetLanguage(NativeLanguage(target))!.Build();
            using var translator = NativeTranslation.GetClient(options);
            try
            {
                using var translated = await translator.Translate(request.Text).AsAsync<Java.Lang.String>();
                cancellationToken.ThrowIfCancellationRequested();
                string text = translated?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(text)) throw new ChatTranslationException(TranslationFailure.Failed);
                return new(text, source, target);
            }
            finally { translator.Close(); }
        }
        catch (OperationCanceledException) { throw; }
        catch (ChatTranslationException) { throw; }
        // Native exceptions can contain input text; expose only a stable, content-free failure.
        catch { throw new ChatTranslationException(TranslationFailure.Failed); }
    }

    public bool HasPendingModelDownload(string language) => modelDownloads.Contains(language);

    public async Task DownloadModelsAsync(IReadOnlyList<string> languages, TranslationDownloadPolicy policy, CancellationToken cancellationToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            foreach (string language in languages)
            {
                if ((await GetDownloadedModelsAsync(wait.Token)).Contains(language)) continue;
                var download = modelDownloads.GetOrStart(language, () =>
                {
                    wait.Token.ThrowIfCancellationRequested();
                    if (policy == TranslationDownloadPolicy.ExistingOnly) return null;
                    var connectivity = Connectivity.Current;
                    if (GetNetworkFailure(policy, connectivity.NetworkAccess, connectivity.ConnectionProfiles) is { } failure)
                        throw new ChatTranslationException(failure);
                    // ML Kit may reattach to a Wi-Fi job from an earlier process.
                    bool mayReuseWifi = policy == TranslationDownloadPolicy.AnyNetwork
                        && Preferences.Default.Get(WifiDownloadKey(language), false);
                    return new(policy, StartModelDownloadAsync(language, policy), mayReuseWifi);
                });
                if (download is not null) await WaitForModelAsync(download, wait.Token, cancellationToken);
                // A task can finish between the first model check and trying to join it.
                else if (!(await GetDownloadedModelsAsync(wait.Token)).Contains(language))
                    throw new ChatTranslationException(TranslationFailure.DownloadFailed);
            }
        }
        catch (ChatTranslationException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { throw new ChatTranslationException(TranslationFailure.DownloadFailed); }
    }

    async Task StartModelDownloadAsync(string language, TranslationDownloadPolicy policy)
    {
        using var model = new TranslateRemoteModel.Builder(NativeLanguage(language)).Build();
        using var builder = new DownloadConditions.Builder();
        bool wifi = policy == TranslationDownloadPolicy.Wifi;
        if (wifi) builder.RequireWifi();
        using var conditions = builder.Build();
        string key = WifiDownloadKey(language);
        if (wifi) Preferences.Default.Set(key, true);
        try { await RemoteModelManager.Instance.Download(model, conditions).AsAsync(); }
        finally { Preferences.Default.Remove(key); }
        // The native task can finish after the chat has stopped waiting for it.
        ModelDownloaded?.Invoke(this, EventArgs.Empty);
    }

    static async Task WaitForModelAsync(TranslationModelDownloads.Download download, CancellationToken token,
        CancellationToken cancellationToken)
    {
        var connectivity = Connectivity.Current;
        var networkLost = new TaskCompletionSource<TranslationFailure>(TaskCreationOptions.RunContinuationsAsynchronously);
        void CheckNetwork(NetworkAccess access, IEnumerable<ConnectionProfile> profiles)
        {
            if (GetNetworkFailure(download.Policy, access, profiles) is { } failure) networkLost.TrySetResult(failure);
        }

        void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) => CheckNetwork(e.NetworkAccess, e.ConnectionProfiles);
        connectivity.ConnectivityChanged += OnConnectivityChanged;
        try
        {
            CheckNetwork(connectivity.NetworkAccess, connectivity.ConnectionProfiles);
            await Task.WhenAny(download.Completion, networkLost.Task).WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (!download.Completion.IsCompleted) throw new ChatTranslationException(await networkLost.Task);
            await download.Completion;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested && !cancellationToken.IsCancellationRequested
            && download.MayReuseWifi && !download.Completion.IsCompleted
            && connectivity.NetworkAccess == NetworkAccess.Internet
            && !connectivity.ConnectionProfiles.Contains(ConnectionProfile.WiFi))
        {
            throw new ChatTranslationException(TranslationFailure.DownloadWifiMayBeRequired);
        }
        finally { connectivity.ConnectivityChanged -= OnConnectivityChanged; }
    }

    static TranslationFailure? GetNetworkFailure(TranslationDownloadPolicy policy, NetworkAccess access, IEnumerable<ConnectionProfile> profiles)
    {
        if (policy == TranslationDownloadPolicy.Wifi && !profiles.Contains(ConnectionProfile.WiFi)) return TranslationFailure.DownloadWifiRequired;
        return access == NetworkAccess.Internet ? null : TranslationFailure.DownloadFailed;
    }

    public async Task<IReadOnlyList<string>> GetDownloadedModelsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var modelClass = Java.Lang.Class.FromType(typeof(TranslateRemoteModel));
        using var result = await RemoteModelManager.Instance.GetDownloadedModels(modelClass).AsAsync<Java.Util.ISet>();
        cancellationToken.ThrowIfCancellationRequested();
        // English is built in and cannot be downloaded or deleted separately.
        var models = result.ToArray()!.Select(item => TranslationLanguages.Normalize(item.JavaCast<TranslateRemoteModel>()!.Language))
            .Where(language => language != "en").Distinct().ToArray();
        foreach (string language in models)
        {
            string key = WifiDownloadKey(language);
            if (Preferences.Default.Get(key, false) && !modelDownloads.Contains(language)) Preferences.Default.Remove(key);
        }
        return models;
    }

    public async Task DeleteModelAsync(string language, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var model = new TranslateRemoteModel.Builder(NativeLanguage(language)).Build();
        await RemoteModelManager.Instance.DeleteDownloadedModel(model).AsAsync();
        Preferences.Default.Remove(WifiDownloadKey(language));
        cancellationToken.ThrowIfCancellationRequested();
    }

    static string WifiDownloadKey(string language) => "onegate.translation.wifi-download." + language;

    static string NativeLanguage(string language) => language == "zh-Hans" ? "zh" : language;
}
#endif
