using NeoOrder.OneGate.Models;

namespace NeoOrder.OneGate.Pages;

public partial class ChatPage
{
    sealed record InlineMessageLink(Label Label, ChatMessageViewModel Message, Uri Uri);
    sealed record InlineMessageAddress(Label Label, ChatMessageViewModel Message, string Address);

    void OnMessageTextBindingContextChanged(object sender, EventArgs e)
    {
        if (sender is not Label label) return;
        label.FormattedText = null;
        label.Text = null;
        if (label.BindingContext is not ChatMessageViewModel message) return;
        string text = message.BubbleText;
        int visibleLength = message.HasTruncatedText ? text.Length - 1 : text.Length;
        FormattedString? formatted = null;
        int offset = 0;
        var links = ChatMessageLinks.FindOccurrences(message.CopyText).ToArray();
        var addresses = addressBookService.FindAddressOccurrences(message.CopyText)
            // An address in a web URL remains part of that URL, not a second tap target.
            .Where(address => !links.Any(link => address.Start < link.Start + link.Length
                && address.Start + address.Length > link.Start));
        var targets = links.Select(link => (link.Start, link.Length,
                Target: (object)new InlineMessageLink(label, message, link.Uri)))
            .Concat(addresses.Select(address => (address.Start, address.Length,
                Target: (object)new InlineMessageAddress(label, message, address.Address))))
            .OrderBy(target => target.Start);
        foreach (var target in targets)
        {
            // Only complete destinations from the original text can be tapped in a shortened bubble.
            if (target.Start + target.Length > visibleLength) break;
            formatted ??= new FormattedString();
            if (target.Start > offset) formatted.Spans.Add(new Span { Text = text[offset..target.Start] });
            var span = new Span { Text = text.Substring(target.Start, target.Length), TextDecorations = TextDecorations.Underline };
            var tap = new TapGestureRecognizer { CommandParameter = target.Target };
            if (target.Target is InlineMessageAddress) tap.Tapped += OnInlineMessageAddressTapped;
            else tap.Tapped += OnInlineMessageLinkTapped;
            span.GestureRecognizers.Add(tap);
            formatted.Spans.Add(span);
            offset = target.Start + target.Length;
        }
        // Plain text needs no span hit regions, which Android rebuilds on each layout.
        if (formatted is null)
        {
            label.Text = text;
            return;
        }
        if (offset < text.Length) formatted.Spans.Add(new Span { Text = text[offset..] });
        label.FormattedText = formatted;
    }

    async void OnInlineMessageAddressTapped(object? sender, TappedEventArgs e)
    {
        if (openingDetails || openingMessageLink || showingMessageActions || sharingMessageText || forwardingMessage
            || !HasLoaded || switchingConversation || IsChatContentCovered || e.Parameter is not InlineMessageAddress target
            || !ReferenceEquals(target.Label.BindingContext, target.Message) || !CanCopyMessage(target.Message)) return;
        var wallet = walletProvider.GetWallet();
        string? accountId = localAccountId;
        string address = Address;
        await OpenMessageAddressAsync(target.Address,
            () => !IsChatContentCovered && IsCurrentMessageContext(wallet, accountId, address)
                && ReferenceEquals(target.Label.BindingContext, target.Message) && CanCopyMessage(target.Message),
            error => ErrorText = error);
    }

    async Task OpenMessageAddressAsync(string address, Func<bool> isCurrent, Action<string> reportError)
    {
        if (openingDetails || openingMessageLink || sharingMessageText || forwardingMessage
            || !HasLoaded || switchingConversation || !isCurrent()) return;
        openingDetails = true;
        contactDetailsHistory = CaptureContactDetailsHistory();
        try
        {
            await Shell.Current.GoToAsync("add-contact", new ShellNavigationQueryParameters { ["input"] = address });
        }
        catch (Exception ex) { if (isCurrent()) reportError(ex.Message); }
        finally
        {
            if (isVisible) contactDetailsHistory = null;
            openingDetails = false;
        }
    }

    async void OnInlineMessageLinkTapped(object? sender, TappedEventArgs e)
    {
        if (showingMessageActions || e.Parameter is not InlineMessageLink target
            || !ReferenceEquals(target.Label.BindingContext, target.Message) || !CanCopyMessage(target.Message)) return;
        await OpenMessageLinkAsync(target.Uri, walletProvider.GetWallet(), localAccountId, Address);
    }
}
