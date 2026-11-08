using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services.Translation;
using System.ComponentModel;

namespace NeoOrder.OneGate.Models;

public sealed class ChatTranslationViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public TranslationResult? Result { get; private set; }
    public string? Error { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsExpanded { get; private set; }
    public bool IsVisible => IsExpanded || Result is not null || Error is not null;
    public string ToggleGlyph => IsExpanded ? "\ue610" : "\ue608";
    public string ToggleDescription => IsExpanded ? Strings.TranslationHide : Strings.TranslationShow;
    bool retryable;
    public bool HasResult => Result is { SameLanguage: false };
    public bool CanRetry => Error is not null && !IsBusy && retryable;
    public bool HasError => Error is not null;
    public string? Text => Result?.Text;
    public string Preview => ChatMessageViewModel.PreviewText(Text ?? "", maxLength: 160, maxLines: 6);
    public bool IsTruncated => HasResult && Preview != Text;
    public string Label => Strings.ChatTranslation + (Result is null ? "" : " · " + TranslationLanguages.DisplayName(Result.TargetLanguage));
    public string? Notice => Result?.SameLanguage == true ? Strings.TranslationSameLanguage : Error;
    public bool HasNotice => Notice is not null;

    public void Start() { IsBusy = IsExpanded = true; Error = null; Result = null; Notify(); }
    public void Complete(TranslationResult result) { Result = result; IsBusy = false; Notify(); }
    public void Fail(string error, bool canRetry) { Error = error; retryable = canRetry; IsBusy = false; Notify(); }
    public void Cancel() { IsBusy = false; IsExpanded = false; Notify(); }
    public void Toggle() { IsExpanded = !IsExpanded; Notify(); }
    void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
