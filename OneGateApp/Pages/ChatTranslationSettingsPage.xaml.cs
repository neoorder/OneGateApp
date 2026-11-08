using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services.Translation;

namespace NeoOrder.OneGate.Pages;

public sealed record TranslationModelRow(string Language, bool HasDivider)
{
    public string Name => TranslationLanguages.DisplayName(Language);
}

public partial class ChatTranslationSettingsPage : ContentPage
{
    readonly IChatTranslationService service;
    readonly TranslationPreferences preferences;
    readonly TranslationScheduler scheduler;
    CancellationTokenSource? lifetime;
    Window? observedWindow;
    string[] targetLanguages = [];
    bool loadingPicker;
    public bool IsAvailable => service.IsAvailable;
    public bool CanManageModels => service.CanManageModels;
    public bool ShowAppleModels => IsAvailable && !CanManageModels;
    public new bool IsBusy { get; private set; }
    public bool IsLoadingLanguages { get; private set; }
    public bool IsWorkingOnModels => IsBusy && CanManageModels && !IsLoadingLanguages;
    public bool CanEdit => IsAvailable && !IsBusy && lifetime is { IsCancellationRequested: false };
    public bool HasLanguageError { get; private set; }
    public bool HasModelReadError { get; private set; }
    public bool HasModelDeleteError { get; private set; }
    public bool HasModelError => HasModelReadError || HasModelDeleteError;
    public string? Notice { get; private set; }
    public TranslationModelRow[] Models { get; private set; } = [];
    public bool HasNoModels => CanManageModels && !IsBusy && !HasModelError && Models.Length == 0;

    public ChatTranslationSettingsPage(IChatTranslationService service, TranslationPreferences preferences, TranslationScheduler scheduler)
    {
        this.service = service;
        this.preferences = preferences;
        this.scheduler = scheduler;
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        StopObservingModels();
        lifetime = new();
        HasModelReadError = false;
        HasModelDeleteError = false;
        if (CanManageModels)
        {
            service.ModelDownloaded += OnModelRefreshRequested;
            observedWindow = Window;
            if (observedWindow is not null) observedWindow.Resumed += OnModelRefreshRequested;
        }
        await LoadAsync(lifetime.Token);
    }

    protected override void OnDisappearing()
    {
        StopObservingModels();
        base.OnDisappearing();
    }

    void StopObservingModels()
    {
        if (CanManageModels) service.ModelDownloaded -= OnModelRefreshRequested;
        if (observedWindow is not null) observedWindow.Resumed -= OnModelRefreshRequested;
        observedWindow = null;
        lifetime?.Cancel();
        lifetime?.Dispose();
        lifetime = null;
    }

    async Task LoadAsync(CancellationToken token)
    {
        IsBusy = true;
        IsLoadingLanguages = IsAvailable;
        HasLanguageError = false;
        Notice = null;
        Notify();
        try
        {
            if (!IsAvailable) return;
            try
            {
                var languages = await service.GetLanguagesAsync(token);
                token.ThrowIfCancellationRequested();
                loadingPicker = true;
                try
                {
                    targetLanguages = ["", .. languages.OrderBy(TranslationLanguages.DisplayName)];
                    targetPicker.ItemsSource = targetLanguages.Select(language => language == "" ? Strings.TranslationFollowLanguage
                        : $"{TranslationLanguages.DisplayName(language)} ({language})").ToArray();
                    targetPicker.SelectedIndex = Array.IndexOf(targetLanguages, preferences.TargetLanguage);
                    UpdateTargetNotice();
                }
                finally { loadingPicker = false; }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch { token.ThrowIfCancellationRequested(); HasLanguageError = true; Notice = Strings.TranslationLanguagesLoadFailed; }
            token.ThrowIfCancellationRequested();
            IsLoadingLanguages = false;
            Notify();
            if (CanManageModels) await RefreshModelsAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { if (!token.IsCancellationRequested) { IsBusy = false; IsLoadingLanguages = false; Notify(); } }
    }

    void OnModelRefreshRequested(object? sender, EventArgs e)
    {
        if (lifetime is not { } current) return;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (!ReferenceEquals(current, lifetime)) return;
            var token = current.Token;
            try { await RefreshModelsAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        });
    }

    Task RefreshModelsAsync(CancellationToken token) => scheduler.RunAsync(async ct =>
    {
        IReadOnlyList<string> models;
        try { models = await service.GetDownloadedModelsAsync(ct); }
        catch
        {
            ct.ThrowIfCancellationRequested();
            SetModelReadError(true);
            return false;
        }
        ct.ThrowIfCancellationRequested();

        UpdateModelRows(models.OrderBy(TranslationLanguages.DisplayName).ToArray());
        SetModelReadError(false);
        return true;
    }, token);

    void UpdateModelRows(string[] languages)
    {
        if (languages.SequenceEqual(Models.Select(row => row.Language))) return;
        Models = languages.Select((language, index) => new TranslationModelRow(language, index < languages.Length - 1)).ToArray();
        OnPropertyChanged(nameof(Models));
        OnPropertyChanged(nameof(HasNoModels));
    }

    void SetModelReadError(bool failed)
    {
        if (HasModelReadError == failed) return;
        HasModelReadError = failed;
        OnPropertyChanged(nameof(HasModelReadError));
        OnPropertyChanged(nameof(HasModelError));
        OnPropertyChanged(nameof(HasNoModels));
    }

    void UpdateTargetNotice() => Notice = TranslationLanguages.Match(preferences.EffectiveTarget, targetLanguages.Skip(1)) is null
        ? Strings.TranslationLanguageUnsupported : null;

    void OnTargetChanged(object? sender, EventArgs e)
    {
        if (loadingPicker || !CanEdit || targetPicker.SelectedIndex < 0 || targetPicker.SelectedIndex >= targetLanguages.Length) return;
        preferences.TargetLanguage = targetLanguages[targetPicker.SelectedIndex];
        HasLanguageError = false;
        UpdateTargetNotice();
        Notify();
    }

    async void OnRetryClicked(object sender, EventArgs e)
    {
        if (CanEdit && lifetime is { } current) await LoadAsync(current.Token);
    }

    async void OnDeleteModelClicked(object sender, EventArgs e)
    {
        if (!CanEdit || lifetime is not { } current || sender is not Button { CommandParameter: string language }
            || !Models.Any(row => row.Language == language)) return;
        var token = current.Token;
        IsBusy = true;
        Notify();
        try
        {
            bool confirmed = await DisplayAlertAsync(Strings.TranslationLanguageModels,
                string.Format(Strings.TranslationDeleteConfirm, TranslationLanguages.DisplayName(language)), Strings.Delete, Strings.Cancel);
            token.ThrowIfCancellationRequested();
            if (!confirmed) return;
            await scheduler.RunAsync(async ct =>
            {
                await service.DeleteModelAsync(language, ct);
                ct.ThrowIfCancellationRequested();
                HasModelDeleteError = false;
                UpdateModelRows(Models.Select(row => row.Language).Where(model => model != language).ToArray());
                return true;
            }, token);
            await RefreshModelsAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch { if (!token.IsCancellationRequested) HasModelDeleteError = true; }
        finally { if (!token.IsCancellationRequested) { IsBusy = false; Notify(); } }
    }

    void Notify()
    {
        foreach (var name in new[] { nameof(IsBusy), nameof(IsLoadingLanguages), nameof(IsWorkingOnModels), nameof(CanEdit),
            nameof(HasLanguageError), nameof(Notice), nameof(HasModelReadError), nameof(HasModelDeleteError), nameof(HasModelError),
            nameof(Models), nameof(HasNoModels) }) OnPropertyChanged(name);
    }
}
