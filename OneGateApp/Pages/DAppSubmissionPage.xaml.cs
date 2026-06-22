using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Pages;

public partial class DAppSubmissionPage : ContentPage
{
    public string? SubmissionProjectName { get; set { field = value; OnPropertyChanged(); } }
    public string? SubmissionIntroduction { get; set { field = value; OnPropertyChanged(); } }
    public string? SubmissionUrl { get; set { field = value; OnPropertyChanged(); } }
    public string? SubmissionWebsite { get; set { field = value; OnPropertyChanged(); } }
    public string? SubmissionContact { get; set { field = value; OnPropertyChanged(); } }

    public DAppSubmissionPage()
    {
        InitializeComponent();
    }

    async void OnSubmitDAppClicked(object sender, EventArgs e)
    {
        string? missingField = GetMissingSubmissionField();
        if (missingField is not null)
        {
            await DisplayAlertAsync(null, string.Format(Strings.DefaultRequiredErrorMessage, missingField), Strings.OK);
            return;
        }

        string projectName = NormalizeSubmissionValue(SubmissionProjectName);
        string introduction = NormalizeSubmissionValue(SubmissionIntroduction);
        string url = NormalizeSubmissionValue(SubmissionUrl);
        string website = NormalizeSubmissionValue(SubmissionWebsite);
        string contact = NormalizeSubmissionValue(SubmissionContact);

        EmailMessage message = new()
        {
            Subject = string.Format(Strings.DAppSubmissionEmailSubject, projectName),
            Body = string.Join(Environment.NewLine, [
                $"{Strings.ProjectName}: {projectName}",
                "",
                $"{Strings.Introduction}:",
                introduction,
                "",
                $"{Strings.DAppUrl}: {url}",
                $"{Strings.OfficialWebsite}: {website}",
                $"{Strings.ContactInformation}: {contact}"
            ]),
            To = [SharedOptions.ContactEmail]
        };

        try
        {
            await Email.ComposeAsync(message);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(null, ex.Message, Strings.OK);
        }
    }

    string? GetMissingSubmissionField()
    {
        if (string.IsNullOrWhiteSpace(SubmissionProjectName)) return Strings.ProjectName;
        if (string.IsNullOrWhiteSpace(SubmissionIntroduction)) return Strings.Introduction;
        if (string.IsNullOrWhiteSpace(SubmissionUrl)) return Strings.DAppUrl;
        if (string.IsNullOrWhiteSpace(SubmissionWebsite)) return Strings.OfficialWebsite;
        if (string.IsNullOrWhiteSpace(SubmissionContact)) return Strings.ContactInformation;
        return null;
    }

    static string NormalizeSubmissionValue(string? value) => value?.Trim() ?? "";
}
