#if ANDROID
using AndroidSoftInput = Android.Views.SoftInput;
#endif
#if WINDOWS
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using NativeKeyboardAccelerator = Microsoft.UI.Xaml.Input.KeyboardAccelerator;
#endif

namespace NeoOrder.OneGate.Pages;

public partial class ChatPage
{
#if ANDROID
    Android.Views.Window? keyboardWindow;
    AndroidSoftInput previousSoftInputMode;
#endif

    void ConfigureChatSoftInput()
    {
#if ANDROID
        if (keyboardWindow is not null
            || Window?.Handler?.PlatformView is not Android.App.Activity { Window: { } window }
            || window.Attributes is not { } attributes) return;
        keyboardWindow = window;
        previousSoftInputMode = attributes.SoftInputMode;
        // Resize this window while chatting; keep the toolbar outside the keyboard area.
        window.SetSoftInputMode((previousSoftInputMode & ~AndroidSoftInput.MaskAdjust) | AndroidSoftInput.AdjustResize);
#endif
    }

    void RestoreChatSoftInput()
    {
#if ANDROID
        if (keyboardWindow is not { } window) return;
        window.SetSoftInputMode(previousSoftInputMode);
        keyboardWindow = null;
#endif
    }

    void OnChatLayoutHandlerChanged(object sender, EventArgs e)
    {
#if WINDOWS
        DetachChatNavigationKeyboard();
        if (chatLayout.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement layout) return;
        nativeChatLayout = layout;
        layout.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        searchMessagesShortcut = new NativeKeyboardAccelerator
        {
            Key = VirtualKey.F,
            Modifiers = VirtualKeyModifiers.Control,
            ScopeOwner = layout
        };
        closeChatOverlayShortcut = new NativeKeyboardAccelerator { Key = VirtualKey.Escape, ScopeOwner = layout };
        foreach (var shortcut in new[] { searchMessagesShortcut, closeChatOverlayShortcut })
        {
            shortcut.Invoked += OnChatNavigationShortcutInvoked;
            layout.KeyboardAccelerators.Add(shortcut);
        }
#endif
    }

    void OnChatLayoutHandlerChanging(object sender, HandlerChangingEventArgs e)
    {
#if WINDOWS
        DetachChatNavigationKeyboard();
#endif
    }

    void OnChatEntryHandlerChanged(object sender, EventArgs e)
    {
#if WINDOWS
        if (sender is not Entry { Handler.PlatformView: TextBox entry }) return;
        DetachChatEntryKeyboard(entry);
        entry.TextCompositionStarted += OnChatEntryCompositionStarted;
        entry.TextCompositionEnded += OnChatEntryCompositionEnded;
#endif
    }

    void OnChatEntryHandlerChanging(object sender, HandlerChangingEventArgs e)
    {
#if WINDOWS
        if (e.OldHandler?.PlatformView is TextBox entry) DetachChatEntryKeyboard(entry);
#endif
    }

    void OnMessageEditorHandlerChanged(object sender, EventArgs e)
    {
#if WINDOWS
        DetachMessageEditorKeyboard();
        if (messageEditor.Handler?.PlatformView is not TextBox editor) return;
        nativeMessageEditor = editor;
        editor.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        sendShortcut = new NativeKeyboardAccelerator
        {
            Key = VirtualKey.Enter,
            Modifiers = VirtualKeyModifiers.Control,
            ScopeOwner = editor
        };
        sendShortcut.Invoked += OnSendShortcutInvoked;
        editor.KeyboardAccelerators.Add(sendShortcut);
        editor.TextCompositionStarted += OnMessageCompositionStarted;
        editor.TextCompositionEnded += OnMessageCompositionEnded;
#endif
    }

    void OnMessageEditorHandlerChanging(object sender, HandlerChangingEventArgs e)
    {
#if WINDOWS
        DetachMessageEditorKeyboard();
#endif
    }

#if WINDOWS
    Microsoft.UI.Xaml.UIElement? nativeChatLayout;
    NativeKeyboardAccelerator? searchMessagesShortcut;
    NativeKeyboardAccelerator? closeChatOverlayShortcut;
    readonly HashSet<TextBox> composingChatEntries = [];
    TextBox? nativeMessageEditor;
    NativeKeyboardAccelerator? sendShortcut;
    bool isComposingMessage;

    void OnChatEntryCompositionStarted(TextBox sender, TextCompositionStartedEventArgs args) => composingChatEntries.Add(sender);
    void OnChatEntryCompositionEnded(TextBox sender, TextCompositionEndedEventArgs args) => composingChatEntries.Remove(sender);

    void DetachChatEntryKeyboard(TextBox entry)
    {
        entry.TextCompositionStarted -= OnChatEntryCompositionStarted;
        entry.TextCompositionEnded -= OnChatEntryCompositionEnded;
        composingChatEntries.Remove(entry);
    }

    void OnChatNavigationShortcutInvoked(NativeKeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Let text composition and open native menus handle their own keys first.
        if (args.Handled || isComposingMessage || composingChatEntries.Count > 0 || !isVisible
            || !ReferenceEquals(Shell.Current?.CurrentPage, this)) return;
        if (ShowForwardRecipientPicker)
        {
            if (ReferenceEquals(sender, closeChatOverlayShortcut)) BackFromForwardRecipientPicker();
            else if (ReferenceEquals(sender, searchMessagesShortcut) && !ShowForwardRecipientPreview)
                forwardRecipientSearchEntry?.Focus();
            else return;
            args.Handled = true;
            return;
        }
        if (showingMessageActions || openingMessageLink || sharingMessageText || forwardingMessage || openingDetails) return;
        if (ReferenceEquals(sender, searchMessagesShortcut))
        {
            if (ShowMessageSearch)
            {
                if (!IsCurrentMessageSearch()) return;
                messageSearchEntry?.Focus();
            }
            else
            {
                if (!CanOpenMessageSearch) return;
                OnOpenMessageSearchClicked(this, EventArgs.Empty);
            }
        }
        else if (ReferenceEquals(sender, closeChatOverlayShortcut))
        {
            if (ShowMessageSearch) CloseMessageSearch();
            else if (ShowMessageReader) CloseMessageReader();
            else return;
        }
        else return;
        args.Handled = true;
    }

    void DetachChatNavigationKeyboard()
    {
        foreach (var shortcut in new[] { searchMessagesShortcut, closeChatOverlayShortcut })
        {
            if (shortcut is null) continue;
            shortcut.Invoked -= OnChatNavigationShortcutInvoked;
            nativeChatLayout?.KeyboardAccelerators.Remove(shortcut);
        }
        nativeChatLayout = null;
        searchMessagesShortcut = null;
        closeChatOverlayShortcut = null;
    }

    void OnMessageCompositionStarted(TextBox sender, TextCompositionStartedEventArgs args) => isComposingMessage = true;
    void OnMessageCompositionEnded(TextBox sender, TextCompositionEndedEventArgs args) => isComposingMessage = false;

    void OnSendShortcutInvoked(NativeKeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Leave the keystroke to the IME while the user is still choosing composed text.
        if (isComposingMessage || !isVisible || !messageEditor.IsFocused) return;
        args.Handled = true;
        OnSendClicked(messageEditor, EventArgs.Empty);
    }

    void DetachMessageEditorKeyboard()
    {
        if (nativeMessageEditor is { } editor)
        {
            if (sendShortcut is { } shortcut)
            {
                shortcut.Invoked -= OnSendShortcutInvoked;
                editor.KeyboardAccelerators.Remove(shortcut);
            }
            editor.TextCompositionStarted -= OnMessageCompositionStarted;
            editor.TextCompositionEnded -= OnMessageCompositionEnded;
        }
        nativeMessageEditor = null;
        sendShortcut = null;
        isComposingMessage = false;
    }
#endif
}
