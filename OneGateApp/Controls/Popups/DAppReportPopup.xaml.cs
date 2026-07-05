using CommunityToolkit.Maui.Alerts;
using NeoOrder.OneGate.Controls.Views;
using NeoOrder.OneGate.Controls.Views.Validation;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace NeoOrder.OneGate.Controls.Popups;

public partial class DAppReportPopup : MyPopup<bool>
{
    readonly HttpClient httpClient;

    public required DApp DApp { get; set { field = value; OnPropertyChanged(); } }
    public DAppReportReasonOption[] Reasons { get; } =
    [
        new("Scam", Strings.ReportReasonScam),
        new("Malware", Strings.ReportReasonMalware),
        new("IllegalContent", Strings.ReportReasonIllegalContent),
        new("InappropriateContent", Strings.ReportReasonInappropriateContent),
        new("Copyright", Strings.ReportReasonCopyright),
        new("Other", Strings.ReportReasonOther)
    ];
    public DAppReportReasonOption? SelectedReason { get; set { field = value; OnPropertyChanged(); } }
    public string? Email { get; set { field = value; OnPropertyChanged(); } }
    public string? Message { get; set { field = value; OnPropertyChanged(); } }

    public DAppReportPopup(HttpClient httpClient)
    {
        this.httpClient = httpClient;
        InitializeComponent();
    }

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailAddressRegex();

    void OnValidateEmail(object sender, CustomValidationEventArgs e)
    {
        string email = ((string)e.Value!).Trim();
        if (!EmailAddressRegex().IsMatch(email))
        {
            e.IsValid = false;
            e.ErrorMessage = Strings.InvalidEmailAddress;
        }
    }

    void OnValidateMessage(object sender, CustomValidationEventArgs e)
    {
        if (e.Value is string message && message.Length > 512)
            e.IsValid = false;
    }

    async void OnSubmit(object sender, EventArgs e)
    {
        SpinnerButton button = (SpinnerButton)sender;
        using (button.EnterBusyState())
        {
            DAppReportRequest request = new(
                SelectedReason!.Reason,
                Email!.Trim(),
                string.IsNullOrWhiteSpace(Message) ? null : Message.Trim());
            HttpResponseMessage response;
            try
            {
                response = await httpClient.PostAsJsonAsync($"/api/dapp/{DApp.Id}/report", request, SharedOptions.JsonSerializerOptions);
            }
            catch
            {
                await Toast.Show(Strings.ReportFailed);
                return;
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                await Toast.Show(Strings.ReportSubmitted);
                await CloseAsync(true);
                return;
            }
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                await Toast.Show(Strings.ReportAlreadySubmitted);
                await CloseAsync(false);
                return;
            }

            await Toast.Show(Strings.ReportFailed);
        }
    }

    async void OnCancel(object sender, EventArgs e)
    {
        await CloseAsync(false);
    }
}

public sealed record DAppReportReasonOption(string Reason, string Text);

sealed record DAppReportRequest(string Reason, string Email, string? Message);
