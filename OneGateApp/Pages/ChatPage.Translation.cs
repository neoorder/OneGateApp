using CommunityToolkit.Maui.Alerts;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services.Messaging;
using NeoOrder.OneGate.Services.Translation;

namespace NeoOrder.OneGate.Pages;

public partial class ChatPage
{
    readonly IChatTranslationService translationService;
    readonly TranslationPreferences translationPreferences;
    readonly TranslationWorkflow translationWorkflow;
    readonly TranslationSession translations = new();
    readonly Dictionary<TranslationKey, ChatTranslationViewModel> translationRows = [];
    int translationRevision;

    public string TranslateActionText => TranslationText.Action;
    public bool ShowGoogleTranslationAttribution => TranslationText.ShowGoogleAttribution;

    TranslationKey GetTranslationKey(ChatMessageViewModel message) => new(localAccountId!, Address,
        message.MessageId is { } id ? $"{message.SenderAccountId}/{id}" : $"pending/{message.PendingId}",
        message.CopyText!, translationPreferences.EffectiveTarget);

    void RestoreTranslations()
    {
        if (localAccountId is null) return;
        foreach (var message in Messages.Where(message => message.CanTranslate))
        {
            var key = GetTranslationKey(message);
            translationRows.TryGetValue(key, out var row);
            message.Translation = row;
        }
    }

    void RememberPendingTranslationIdentity(PendingChatMessage pending)
    {
        if (pending.SendStatus?.MessageId is not { } id) return;
        var oldKey = new TranslationKey(pending.LocalAccountId, Address, $"pending/{pending.Id}", pending.Text, translationPreferences.EffectiveTarget);
        if (translationRows.TryGetValue(oldKey, out var row))
            translationRows[oldKey with { Message = $"{pending.LocalAccountId}/{id}" }] = row;
    }

    void ClearTranslations()
    {
        StopTranslationLayout();
        translationRevision++;
        translations.Reset();
        translationRows.Clear();
        foreach (var message in Messages) message.Translation = null;
    }

    void OnTranslationPreferenceChanged(object? sender, EventArgs e) => MainThread.BeginInvokeOnMainThread(ClearTranslations);

    async void OnTranslateMessageClicked(object sender, EventArgs e)
    {
        if (GetMessageActionParameter(sender) is { } message) await TranslateMessageAsync(message);
    }

    async Task TranslateMessageAsync(ChatMessageViewModel message)
    {
        if (!translationService.IsAvailable || !message.CanTranslate || !CanCopyMessage(message) || IsChatContentCovered || switchingConversation) return;
        var key = GetTranslationKey(message);
        if (translationRows.TryGetValue(key, out var existing) && (existing.IsBusy || existing.Result is not null))
        {
            if (!existing.IsBusy) ChangeTranslationLayout(existing.Toggle);
            return;
        }
        var row = existing ?? new ChatTranslationViewModel();
        translationRows[key] = row;
        message.Translation = row;
        int revision = translationRevision;
        var wallet = walletProvider.GetWallet();
        bool IsCurrent() => revision == translationRevision && !switchingConversation
            && IsCurrentMessageContext(wallet, key.Account, key.Conversation);
        ChangeTranslationLayout(row.Start);
        try
        {
            var result = await translations.TranslateAsync(key, async token =>
            {
                token.ThrowIfCancellationRequested();
                if (!IsCurrent()) throw new OperationCanceledException();
                return await translationWorkflow.RunAsync(new(key.Text, key.TargetLanguage), new(
                    (source, languages, ct) => PickTranslationLanguageAsync(source ? Strings.TranslationChooseSource : Strings.TranslationChooseTarget, languages, ct),
                    async (languages, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        string choice = await DisplayActionSheetAsync(Strings.TranslationDownloadPrompt + "\n"
                            + string.Join(", ", languages.Select(TranslationLanguages.DisplayName)), Strings.Cancel, null,
                            Strings.TranslationWifi, Strings.TranslationAnyNetwork);
                        ct.ThrowIfCancellationRequested();
                        return choice == Strings.TranslationWifi ? TranslationDownloadPolicy.Wifi
                            : choice == Strings.TranslationAnyNetwork ? TranslationDownloadPolicy.AnyNetwork : null;
                    }), token);
            });
            if (IsCurrent()) ChangeTranslationLayout(() => row.Complete(result));
        }
        catch (OperationCanceledException) { if (IsCurrent()) ChangeTranslationLayout(row.Cancel); }
        catch (Exception ex)
        {
            if (IsCurrent()) ChangeTranslationLayout(() => row.Fail(TranslationText.Error(ex),
                ex is not ChatTranslationException { Failure: TranslationFailure.Unavailable or TranslationFailure.NoText
                    or TranslationFailure.UnsupportedLanguage or TranslationFailure.UnsupportedSourceLanguage
                    or TranslationFailure.UnsupportedTargetLanguage or TranslationFailure.UnsupportedLanguagePair }));
        }
    }

    async Task<string?> PickTranslationLanguageAsync(string title, IReadOnlyList<string> languages, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var sorted = languages.OrderBy(TranslationLanguages.DisplayName).ToArray();
        var labels = sorted.Select(language => $"{TranslationLanguages.DisplayName(language)} ({language})").ToArray();
        string choice = await DisplayActionSheetAsync(title, Strings.Cancel, null, labels);
        token.ThrowIfCancellationRequested();
        int index = Array.IndexOf(labels, choice);
        return index < 0 ? null : sorted[index];
    }

    async void OnCopyTranslationClicked(object sender, EventArgs e)
    {
        if (GetMessageActionParameter(sender) is not { Translation: { HasResult: true } row } message || !CanCopyMessage(message)) return;
        try { await Clipboard.Default.SetTextAsync(row.Text!); }
        catch { await Toast.Show(Strings.OperationFailed); }
    }

    void OnReadTranslationClicked(object sender, EventArgs e)
    {
        if (GetMessageActionParameter(sender) is { Translation: { HasResult: true } row } message && CanCopyMessage(message))
            OpenMessageReader(row.Text, ShowGoogleTranslationAttribution);
    }

    void OnToggleTranslationClicked(object sender, EventArgs e)
    {
        if (GetMessageActionParameter(sender) is { Translation: { IsBusy: false } row } message && CanCopyMessage(message))
            ChangeTranslationLayout(row.Toggle);
    }
}
