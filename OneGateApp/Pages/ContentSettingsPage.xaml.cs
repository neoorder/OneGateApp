using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Pages;

public partial class ContentSettingsPage : ContentPage
{
    readonly ApplicationDbContext dbContext;
    bool settingsLoaded;
    bool updatingSwitch;

    public LoadingService LoadingService { get; set { field = value; OnPropertyChanged(); } }
    public bool AllowRestrictedContent { get; set { field = value; OnPropertyChanged(); } }
    public bool IsSettingsLoaded { get; private set { field = value; OnPropertyChanged(); } }
    public bool IsBirthDateSelectionVisible { get; private set { field = value; OnPropertyChanged(); } }
    public bool IsRestrictedContentConfirmationVisible { get; private set { field = value; OnPropertyChanged(); } }
    public DateTime BirthDate { get; set { field = value; OnPropertyChanged(); } } = DateTime.Today;
    public DateTime MinimumBirthDate { get; } = DateTime.Today.AddYears(-120);
    public DateTime MaximumBirthDate { get; } = DateTime.Today;

    public ContentSettingsPage(ApplicationDbContext dbContext)
    {
        this.LoadingService = new(LoadSettingsAsync);
        this.dbContext = dbContext;
        InitializeComponent();
        LoadingService.BeginLoad();
    }

    async Task LoadSettingsAsync()
    {
        settingsLoaded = false;
        IsSettingsLoaded = false;
        AllowRestrictedContent = await DAppContentPolicy.GetAllowRestrictedContentAsync(dbContext);
        settingsLoaded = true;
        IsSettingsLoaded = true;
    }

    async void OnAllowRestrictedContentToggled(object sender, ToggledEventArgs e)
    {
        if (!settingsLoaded || updatingSwitch) return;
        if (e.Value)
        {
            BirthDate = DateTime.Today;
            IsBirthDateSelectionVisible = true;
            IsRestrictedContentConfirmationVisible = false;
            return;
        }

        HideRestrictedContentFlow();
        await SetAllowRestrictedContentAsync(false);
    }

    async void OnBirthDateContinueClicked(object sender, EventArgs e)
    {
        if (!DAppContentPolicy.IsOldEnoughForRestrictedContent(BirthDate))
        {
            HideRestrictedContentFlow();
            SetSwitchWithoutSaving(false);
            await DisplayAlertAsync(null, Strings.RestrictedContentAgeRejected, Strings.OK);
            return;
        }

        IsBirthDateSelectionVisible = false;
        IsRestrictedContentConfirmationVisible = true;
    }

    void OnRestrictedContentFlowCanceled(object sender, EventArgs e)
    {
        HideRestrictedContentFlow();
        SetSwitchWithoutSaving(false);
    }

    async void OnRestrictedContentConfirmed(object sender, EventArgs e)
    {
        HideRestrictedContentFlow();
        await SetAllowRestrictedContentAsync(true);
    }

    async Task SetAllowRestrictedContentAsync(bool value)
    {
        await dbContext.Settings.PutAsync(DAppContentPolicy.AllowRestrictedContentKey, value);
        SetSwitchWithoutSaving(value);
        GlobalStates.Invalidate<SettingsPage>();
        GlobalStates.Invalidate<DAppsPage>();
        GlobalStates.Invalidate<GamingPage>();
    }

    void SetSwitchWithoutSaving(bool value)
    {
        updatingSwitch = true;
        AllowRestrictedContent = value;
        updatingSwitch = false;
    }

    void HideRestrictedContentFlow()
    {
        IsBirthDateSelectionVisible = false;
        IsRestrictedContentConfirmationVisible = false;
    }
}
