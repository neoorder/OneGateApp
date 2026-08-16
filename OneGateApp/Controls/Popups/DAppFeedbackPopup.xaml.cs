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

public partial class DAppFeedbackPopup : MyPopup<bool>
{
    readonly HttpClient httpClient;

    public required DApp DApp { get; set { field = value; OnPropertyChanged(); } }
    public DAppFeedbackTypeOption[] Types { get; } =
    [
        new("Suggestion", Strings.FeedbackTypeSuggestion),
        new("Problem", Strings.FeedbackTypeProblem),
        new("Other", Strings.FeedbackTypeOther)
    ];
    public DAppFeedbackTypeOption? SelectedType { get; set { field = value; OnPropertyChanged(); } }
    public string? Message { get; set { field = value; OnPropertyChanged(); } }
    public string? Email { get; set { field = value; OnPropertyChanged(); } }

    public DAppFeedbackPopup(HttpClient httpClient)
    {
        this.httpClient = httpClient;
        InitializeComponent();
    }

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailAddressRegex();

    void OnValidateEmail(object sender, CustomValidationEventArgs e)
    {
        string? email = (e.Value as string)?.Trim();
        if (!string.IsNullOrEmpty(email) && !EmailAddressRegex().IsMatch(email))
        {
            e.IsValid = false;
            e.ErrorMessage = Strings.InvalidEmailAddress;
        }
    }

    async void OnSubmit(object sender, EventArgs e)
    {
        SpinnerButton button = (SpinnerButton)sender;
        using (button.EnterBusyState())
        {
            string? email = Email?.Trim();
            DAppFeedbackRequest request = new(
                SelectedType!.Type,
                Message!.Trim(),
                string.IsNullOrEmpty(email) ? null : email);
            HttpResponseMessage response;
            try
            {
                response = await httpClient.PostAsJsonAsync($"/api/dapp/{DApp.Id}/feedback", request, SharedOptions.JsonSerializerOptions);
            }
            catch
            {
                await Toast.Show(Strings.FeedbackFailed);
                return;
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                await Toast.Show(Strings.FeedbackSubmitted);
                await CloseAsync(true);
                return;
            }
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                await Toast.Show(Strings.FeedbackRetryLater);
                await CloseAsync(false);
                return;
            }

            await Toast.Show(Strings.FeedbackFailed);
        }
    }

    async void OnCancel(object sender, EventArgs e)
    {
        await CloseAsync(false);
    }
}

public sealed record DAppFeedbackTypeOption(string Type, string Text);

sealed record DAppFeedbackRequest(string Type, string Message, string? Email);
