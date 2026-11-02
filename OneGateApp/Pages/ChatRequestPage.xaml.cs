using Meshline.Models.Client;
using Meshline.Models.Protocol;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services.Messaging;
using NeoOrder.OneGate.Services;
using System.Text;

namespace NeoOrder.OneGate.Pages;

public partial class ChatRequestPage : ContentPage, IQueryAttributable
{
    enum RequestAction { Load, Send, Accept, Ignore }

    readonly MessagingService messagingService;
    readonly AddressBookService addressBookService;
    readonly IDispatcherTimer expiryTimer;
    CancellationTokenSource? operation;
    Task loadTask = Task.CompletedTask;
    ChatContactDetails? details;
    string? label;
    bool hasLoadedLabel;
    string? invitationValue;
    ContactInvite? invitation;
    bool isPageVisible;
    bool reloadPending;
    bool isRefreshing;
    bool isSilentRefresh;
    bool isNavigating;

    public string Address { get; private set; } = "";
    public string? DisplayName => MessagingService.ResolveChatDisplayName(label, details?.Profile?.Nickname);
    public string? Nickname => IsChatEnabled && details?.Profile?.Nickname != DisplayName ? details?.Profile?.Nickname : null;
    public string? Bio => IsChatEnabled ? details?.Profile?.Bio : null;
    public string? Note
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(ErrorText));
            OnPropertyChanged(nameof(CanRetry));
        }
    }
    public string? ErrorText
    {
        get
        {
            if (IsChatEnabled && details is { CanCreateRequest: true })
            {
                if (IsInvitationExpired) return Strings.ChatReceivedInvitationExpired;
                if (IsNoteTooLong) return Strings.ChatRequestNoteTooLong;
            }
            return field;
        }
        private set;
    }
    bool HasCurrentDetails => messagingService.IsEnabled && details is not null
        && details.LocalAccountId == messagingService.AccountId && details.Address == Address;
    bool IsRequestBusy => operation is not null;
    public bool ShowLoading => IsRequestBusy && (!isSilentRefresh || !HasCurrentDetails);
    public bool CanUseDetails => isPageVisible && ReferenceEquals(Shell.Current?.CurrentPage, this)
        && IsChatEnabled && HasCurrentDetails && (!IsRequestBusy || isRefreshing) && !isNavigating;
    bool IsChatEnabled { get; set; }
    public bool CanRetry => isPageVisible && !IsRequestBusy && !isNavigating && ErrorText is not null;
    public string BusyText { get; private set; } = Strings.Loading;
    public string? Status => IsChatEnabled ? details?.Status : null;
    public bool HasIncomingRequest => IsChatEnabled && details is { IsContact: false, Request.Direction: ContactRequestDirection.Incoming };
    public bool CanRespond => CanUseDetails && HasIncomingRequest;
    public string? IncomingNote => HasIncomingRequest ? details!.Request!.Note : null;
    public bool CanOpenChat => IsChatEnabled && details is { IsContact: true };
    public bool ShowRequestNote => IsChatEnabled && details is { IsContact: false } && !HasIncomingRequest;
    public bool CanEditNote => CanUseDetails && details is { CanCreateRequest: true };
    public bool ShowSend => IsChatEnabled && details is { CanCreateRequest: true }
        && (invitationValue is null ? details.Profile is { PublicDiscovery: true } : invitation is not null);
    public bool CanSend => CanUseDetails && ShowSend && !IsInvitationExpired && !IsNoteTooLong;
    bool IsNoteTooLong => Encoding.UTF8.GetByteCount(Note?.Trim() ?? "") > 1024;
    bool IsInvitationExpired => invitation is not null && invitation.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public string? InvitationExpiry => IsChatEnabled && invitation is not null
        ? string.Format(Strings.ChatInvitationExpiry, DateTimeOffset.FromUnixTimeSeconds(invitation.ExpiresAt).ToLocalTime().ToString("g")) : null;
    public string SendText => IsChatEnabled && details?.Request?.Direction == ContactRequestDirection.Outgoing
        ? Strings.Retry : Strings.ChatSendRequest;

    public ChatRequestPage(MessagingService messagingService, AddressBookService addressBookService)
    {
        this.messagingService = messagingService;
        this.addressBookService = addressBookService;
        expiryTimer = Dispatcher.CreateTimer();
        expiryTimer.IsRepeating = false;
        expiryTimer.Tick += (_, _) => UpdateInvitationExpiry();
        InitializeComponent();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        string? nextInvitation = null;
        string nextAddress = "";
        if (query.TryGetValue("invitation", out var value) && value is string invitationText)
        {
            nextInvitation = invitationText;
            if (invitationValue == nextInvitation) return;
        }
        else if (query.TryGetValue("address", out var address) && address is string addressText)
        {
            nextAddress = addressText.Trim();
            if (invitationValue is null && Address == nextAddress) return;
        }
        else return;

        // Shell can reuse the page for a different address or invitation.
        reloadPending = false;
        operation?.Cancel();
        operation = null;
        CancelRequestProfileLoad();
        details = null;
        label = null;
        hasLoadedLabel = false;
        Note = null;
        invitation = null;
        invitationValue = nextInvitation;
        Address = nextAddress;
        IsChatEnabled = false;
        ErrorText = null;
        NotifyState();
        if (isPageVisible) _ = loadTask = RunAsync(RequestAction.Load);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        isPageVisible = true;
        messagingService.Changed += OnMessagingChanged;
        if (!IsRequestBusy) await (loadTask = RunAsync(RequestAction.Load));
    }

    protected override void OnDisappearing()
    {
        isPageVisible = false;
        reloadPending = false;
        hasLoadedLabel = false;
        messagingService.Changed -= OnMessagingChanged;
        operation?.Cancel();
        operation = null;
        CancelRequestProfileLoad();
        IsChatEnabled = false;
        NotifyState();
        base.OnDisappearing();
    }

    async void OnRetryClicked(object sender, EventArgs e)
    {
        if (CanRetry) await (loadTask = RunAsync(RequestAction.Load));
    }

    void OnMessagingChanged(object? sender, EventArgs e)
    {
        ClearStaleDetails();
        if (!HasCurrentDetails) IsChatEnabled = false;
        NotifyState();
        reloadPending = true;
        ReloadIfIdle();
    }

    async void ReloadIfIdle()
    {
        if (!isPageVisible || !reloadPending || IsRequestBusy) return;
        reloadPending = false;
        await (loadTask = RunAsync(RequestAction.Load, localOnly: true));
    }

    async void OnSendClicked(object sender, EventArgs e)
    {
        UpdateInvitationExpiry();
        if (CanSend) await RunAsync(RequestAction.Send);
    }

    async void OnAcceptClicked(object sender, EventArgs e)
    {
        if (CanRespond) await RunAsync(RequestAction.Accept);
    }

    async void OnIgnoreClicked(object sender, EventArgs e)
    {
        if (CanRespond) await RunAsync(RequestAction.Ignore);
    }

    async void OnOpenChatClicked(object sender, EventArgs e)
    {
        if (!CanUseDetails || !CanOpenChat || details is not { } contact) return;
        string? targetInvitation = invitationValue;
        isNavigating = true;
        ErrorText = null;
        NotifyState();
        try
        {
            await Shell.Current.GoToAsync("//home/chat",
                new ShellNavigationQueryParameters { ["address"] = contact.Address });
        }
        catch (Exception ex)
        {
            if (isPageVisible && HasCurrentDetails && details!.LocalAccountId == contact.LocalAccountId
                && Address == contact.Address && invitationValue == targetInvitation)
                ErrorText = ex.Message;
        }
        finally
        {
            isNavigating = false;
            NotifyState();
        }
    }

    async Task RunAsync(RequestAction action, bool localOnly = false)
    {
        bool send = action == RequestAction.Send;
        bool respond = action is RequestAction.Accept or RequestAction.Ignore;
        if (respond && !CanRespond) return;
        if (IsRequestBusy && !(isRefreshing && (send && CanSend || respond && CanRespond))) return;
        if (action == RequestAction.Load && !localOnly) CancelRequestProfileLoad();
        var actionContact = send || respond ? details : null;
        var pendingRefresh = operation;
        Task pendingLoad = pendingRefresh is not null ? loadTask : Task.CompletedTask;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        isRefreshing = action == RequestAction.Load && HasCurrentDetails;
        isSilentRefresh = isRefreshing && localOnly;
        pendingRefresh?.Cancel();
        IsChatEnabled = HasCurrentDetails;
        ClearStaleDetails();
        ErrorText = null;
        BusyText = action switch
        {
            RequestAction.Send => Strings.ChatRequestSending,
            RequestAction.Accept => Strings.ChatRequestAccepting,
            RequestAction.Ignore => Strings.ChatRequestIgnoring,
            _ => Strings.Loading
        };
        NotifyState();
        bool hasChatState = false;
        try
        {
            // Let a canceled refresh release its storage work before changing the request.
            await pendingLoad;
            cancellation.Token.ThrowIfCancellationRequested();
            await messagingService.InitializeAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            hasChatState = true;
            IsChatEnabled = messagingService.IsEnabled;
            ClearStaleDetails();
            if (!IsChatEnabled)
            {
                details = null;
                Note = null;
                ErrorText = Strings.ChatEnableFirst;
                return;
            }
            if (invitationValue is not null && invitation is null)
            {
                invitation = messagingService.ParseChatInvitation(invitationValue);
                Address = invitation.Inviter[(invitation.Inviter.LastIndexOf(':') + 1)..];
            }
            NotifyState();
            if (respond)
            {
                var request = actionContact!.Request!;
                await messagingService.RespondToContactRequestAsync(actionContact.LocalAccountId, actionContact.Address,
                    request.MessageId, request.CreatedAt, action == RequestAction.Accept, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                var actual = await messagingService.GetChatContactAsync(actionContact.Address, resolveProfile: false, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                SetDetails(actual);
                if (action == RequestAction.Ignore) await Shell.Current.GoToAsync("..");
            }
            else if (send)
            {
                var sent = await messagingService.SendContactRequestAsync(actionContact!.LocalAccountId, Address, Note, cancellation.Token, invitationValue);
                cancellation.Token.ThrowIfCancellationRequested();
                SetDetails(sent);
            }
            else
            {
                // Background request updates can reuse even an absent local label.
                if (!localOnly || !hasLoadedLabel)
                {
                    var savedContact = await addressBookService.FindByAddressAsync(Address, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    label = string.IsNullOrWhiteSpace(savedContact?.Label) ? null : savedContact.Label;
                    hasLoadedLabel = true;
                }
                var local = await messagingService.GetChatContactAsync(Address, resolveProfile: false, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                SetDetails(local);
                NotifyState();
                if (!localOnly && invitationValue is null && local.CanCreateRequest)
                {
                    var resolved = await messagingService.GetChatContactAsync(Address, cancellationToken: cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    // Only a completed profile lookup can replace or clear the known profile.
                    SetDetails(resolved, preserveProfile: !resolved.CanCreateRequest);
                    if (resolved.CanCreateRequest && resolved.Profile is not { PublicDiscovery: true })
                        ErrorText = Strings.ChatAddressNotDiscoverable;
                }
                else if (!localOnly)
                {
                    // Received/sent requests and invitations need profile display, not discovery authorization.
                    _ = LoadRequestProfileAsync(local);
                }
            }
        }
        catch (Exception) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            IsChatEnabled = messagingService.IsEnabled && (hasChatState || HasCurrentDetails);
            ClearStaleDetails();
            ErrorText = ex.Message;
            if ((send || respond) && IsChatEnabled)
            {
                // A request change may have committed before failure. Reload its actual status.
                try
                {
                    var actual = await messagingService.GetChatContactAsync(Address, resolveProfile: false, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    SetDetails(actual);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                catch { details = null; }
            }
        }
        finally
        {
            if (ReferenceEquals(operation, cancellation))
            {
                operation = null;
                isRefreshing = false;
                isSilentRefresh = false;
                NotifyState();
                ReloadIfIdle();
            }
        }
    }

    void ClearStaleDetails()
    {
        if (details is null || HasCurrentDetails) return;
        CancelRequestProfileLoad();
        details = null;
        label = null;
        hasLoadedLabel = false;
        Note = null;
    }

    void SetDetails(ChatContactDetails value, bool preserveProfile = true)
    {
        bool contactChanged = details is not null
            && (details.LocalAccountId != value.LocalAccountId || details.Address != value.Address);
        // Sending and local status updates do not resolve profiles. Retain display data for this contact only.
        if (preserveProfile && !contactChanged && value.Profile is null)
            value = value with { Profile = details?.Profile };
        if (contactChanged) Note = null;
        // Incoming notes belong to the peer; keep them separate from our editable outgoing note.
        if (value.Request is { Direction: ContactRequestDirection.Outgoing } request && (contactChanged || !value.CanCreateRequest
            || details?.Request?.MessageId != request.MessageId))
            Note = request.Note;
        details = value;
    }

    void UpdateInvitationExpiry()
    {
        expiryTimer.Stop();
        if (isPageVisible && IsChatEnabled && details is { CanCreateRequest: true } && invitation is not null
            && DateTimeOffset.FromUnixTimeSeconds(invitation.ExpiresAt) - DateTimeOffset.UtcNow is { Ticks: > 0 } remaining)
        {
            // A local one-shot check; no relay queries or changes to an already sent request.
            expiryTimer.Interval = remaining > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : remaining;
            expiryTimer.Start();
        }
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(CanRetry));
    }

    void NotifyState()
    {
        UpdateInvitationExpiry();
        OnPropertyChanged(nameof(InvitationExpiry));
        OnPropertyChanged(nameof(Address));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Nickname));
        OnPropertyChanged(nameof(Bio));
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(CanUseDetails));
        OnPropertyChanged(nameof(BusyText));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(HasIncomingRequest));
        OnPropertyChanged(nameof(CanRespond));
        OnPropertyChanged(nameof(IncomingNote));
        OnPropertyChanged(nameof(CanOpenChat));
        OnPropertyChanged(nameof(ShowRequestNote));
        OnPropertyChanged(nameof(CanEditNote));
        OnPropertyChanged(nameof(ShowSend));
        OnPropertyChanged(nameof(SendText));
    }

    CancellationTokenSource? profileCancellation;

    void CancelRequestProfileLoad()
    {
        profileCancellation?.Cancel();
        profileCancellation = null;
    }

    async Task LoadRequestProfileAsync(ChatContactDetails contact)
    {
        CancelRequestProfileLoad();
        using var cancellation = new CancellationTokenSource();
        profileCancellation = cancellation;
        try
        {
            // Optional display data uses the SDK cache independently of request actions.
            var profile = await messagingService.GetChatProfileAsync(contact.LocalAccountId, contact.Address, cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(profileCancellation, cancellation)
                || !isPageVisible || !HasCurrentDetails || details!.LocalAccountId != contact.LocalAccountId
                || Address != contact.Address) return;
            // Acceptance or a new request may have arrived while the profile was loading.
            SetDetails(details with { Profile = profile }, preserveProfile: false);
            NotifyState();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Keep the address, introduction and response actions usable without optional metadata.
            if (!cancellation.IsCancellationRequested)
                System.Diagnostics.Debug.WriteLine($"[Meshline] Request profile unavailable: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(profileCancellation, cancellation)) profileCancellation = null;
        }
    }
}
