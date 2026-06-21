using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using System.Globalization;

namespace NeoOrder.OneGate.Pages;

public partial class ActivityCenterPage : ContentPage
{
    readonly ActivityLogService activityLogService;

    public Command RefreshCommand { get; }
    public bool IsRefreshing { get; set { field = value; OnPropertyChanged(); } }
    public ActivityCenterItem[] Activities
    {
        get;
        private set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasActivities));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(ConnectionCount));
            OnPropertyChanged(nameof(SignatureCount));
            OnPropertyChanged(nameof(TransactionCount));
            OnPropertyChanged(nameof(VaultCount));
            OnPropertyChanged(nameof(LastActivityText));
        }
    } = [];

    public bool HasActivities => Activities.Length > 0;
    public bool IsEmpty => !HasActivities;
    public int ConnectionCount => Activities.Count(p => p.Kind is ActivityRecordKind.DAppConnection or ActivityRecordKind.WalletAuthorization);
    public int SignatureCount => Activities.Count(p => p.Kind == ActivityRecordKind.Signature);
    public int TransactionCount => Activities.Count(p => p.Kind == ActivityRecordKind.Transaction);
    public int VaultCount => Activities.Count(p => p.Kind == ActivityRecordKind.VaultOperation);
    public string LastActivityText => HasActivities
        ? string.Format(Strings.LastActivityAt, Activities[0].CreatedAtText)
        : Strings.NoRecentActivity;

    public ActivityCenterPage(ActivityLogService activityLogService)
    {
        this.activityLogService = activityLogService;
        RefreshCommand = new Command(async () => await RefreshAsync());
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshCommand.Execute(null);
    }

    async Task RefreshAsync()
    {
        try
        {
            Activities = (await activityLogService.GetRecentAsync())
                .Select(p => new ActivityCenterItem(p))
                .ToArray();
        }
        finally
        {
            IsRefreshing = false;
        }
    }
}

public sealed class ActivityCenterItem
{
    readonly ActivityRecord record;

    public ActivityCenterItem(ActivityRecord record)
    {
        this.record = record;
    }

    public ActivityRecordKind Kind => record.Kind;
    public string CreatedAtText => record.CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public string IconGlyph => Kind switch
    {
        ActivityRecordKind.DAppConnection => "\ue60c",
        ActivityRecordKind.WalletAuthorization => "\ue738",
        ActivityRecordKind.Signature => "\ue67f",
        ActivityRecordKind.Transaction => "\ue64e",
        ActivityRecordKind.VaultOperation => "\ue927",
        _ => "\ue625"
    };
    public string KindText => Kind switch
    {
        ActivityRecordKind.DAppConnection => Strings.Connection,
        ActivityRecordKind.WalletAuthorization => Strings.WalletAuthorization,
        ActivityRecordKind.Signature => Strings.Signature,
        ActivityRecordKind.Transaction => Strings.Transaction,
        ActivityRecordKind.VaultOperation => Strings.VaultOperation,
        _ => Strings.Activity
    };
    public string Title => Kind switch
    {
        ActivityRecordKind.DAppConnection => string.Format(Strings.ActivityDAppConnectionTitle, DAppName),
        ActivityRecordKind.WalletAuthorization => string.Format(Strings.ActivityWalletAuthorizationTitle, DAppName),
        ActivityRecordKind.Signature => string.Format(Strings.ActivitySignatureTitle, DAppName),
        ActivityRecordKind.Transaction => string.Format(Strings.ActivityTransactionTitle, DAppName),
        ActivityRecordKind.VaultOperation => string.Format(Strings.ActivityVaultOperationTitle, DAppName),
        _ => DAppName
    };
    public string Description
    {
        get
        {
            string source = string.IsNullOrWhiteSpace(record.DAppHost) ? Strings.UnknownSource : record.DAppHost;
            if (!string.IsNullOrWhiteSpace(record.TransactionHash))
                return string.Format(Strings.ActivityTransactionDescription, ShortHash(record.TransactionHash), source);
            return string.Format(Strings.ActivitySourceDescription, source);
        }
    }

    string DAppName => string.IsNullOrWhiteSpace(record.DAppName) ? Strings.UnknownDApp : record.DAppName;

    static string ShortHash(string hash)
    {
        if (hash.Length <= 14) return hash;
        return $"{hash[..8]}...{hash[^6..]}";
    }
}
