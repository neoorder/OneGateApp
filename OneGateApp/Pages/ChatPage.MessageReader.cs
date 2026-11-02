namespace NeoOrder.OneGate.Pages;

public partial class ChatPage
{
    int messageReaderRevision;
    Editor? messageReaderEditor;

    public Command BackCommand { get; }
    public string? MessageReaderText
    {
        get;
        private set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowMessageReader));
            OnPropertyChanged(nameof(IsChatContentCovered));
            OnPropertyChanged(nameof(CanOpenMessageSearch));
        }
    }
    public bool ShowMessageReader => MessageReaderText is not null;

    void OnReadMessageClicked(object sender, EventArgs e)
    {
        if (GetMessageActionParameter(sender) is { } message && CanCopyMessage(message))
            OpenMessageReader(message.CopyText);
    }

    void OpenMessageReader(string? text)
    {
        var wallet = walletProvider.GetWallet();
        if (IsChatContentCovered || string.IsNullOrEmpty(text) || !HasLoaded || switchingConversation
            || !IsCurrentMessageContext(wallet, localAccountId, Address)) return;
        CancelQuoteNavigation();
        scrollRevision++;
        int revision = ++messageReaderRevision;
        var accountId = localAccountId;
        var address = Address;
        // Background refreshes must not replace the text or selection being read.
        messageReaderEditor ??= EnsureChatView(messageReaderView).FindByName<Editor>("messageReaderEditor");
        MessageReaderText = text;
        messageEditor.Unfocus();
        // Run after text binding: native editors can move the caret to the end when assigning text.
        Dispatcher.Dispatch(() =>
        {
            if (revision != messageReaderRevision || !ShowMessageReader || !HasLoaded || switchingConversation
                || !IsCurrentMessageContext(wallet, accountId, address)) return;
            ResetMessageTextPosition(messageReaderEditor);
        });
    }

    static void ResetMessageTextPosition(Editor editor)
    {
        editor.SelectionLength = 0;
        editor.CursorPosition = 0;
#if ANDROID
        if (editor.Handler?.PlatformView is Android.Widget.EditText native)
        {
            // MAUI's cursor mapper skips read-only Android editors.
            native.SetSelection(0);
            native.ScrollTo(0, 0);
        }
#elif WINDOWS
        if (editor.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox native)
        {
            native.Select(0, 0);
            FindNativeScrollViewer(native)?.ChangeView(0, 0, null, disableAnimation: true);
        }
#elif IOS || MACCATALYST
        if (editor.Handler?.PlatformView is UIKit.UITextView native)
        {
            native.SelectedRange = new Foundation.NSRange(0, 0);
            native.SetContentOffset(new CoreGraphics.CGPoint(-native.AdjustedContentInset.Left, -native.AdjustedContentInset.Top), animated: false);
        }
#endif
    }

    void CloseMessageReader()
    {
        if (!ShowMessageReader) return;
        ClearMessageReader();
        if (HasLoaded && followLatest && !switchingConversation
            && IsCurrentMessageContext(walletProvider.GetWallet(), localAccountId, Address))
        {
            // Only a confirmed viewport may mark arrivals hidden behind the reader as read.
            QueueLatestMessageScroll();
        }
    }

    void ClearMessageReader()
    {
        messageReaderRevision++;
        MessageReaderText = null;
    }

    protected override bool OnBackButtonPressed()
    {
        if (ShowForwardRecipientPicker)
        {
            BackFromForwardRecipientPicker();
            return true;
        }
        if (ShowMessageSearch)
        {
            CloseMessageSearch();
            return true;
        }
        if (!ShowMessageReader) return base.OnBackButtonPressed();
        CloseMessageReader();
        return true;
    }
}
