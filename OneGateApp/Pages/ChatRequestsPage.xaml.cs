using CommunityToolkit.Maui.Alerts;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.Messaging;

namespace NeoOrder.OneGate.Pages;

public partial class ChatRequestsPage : ContentPage
{
    readonly MessagingService messagingService;
    readonly AddressBookService addressBookService;
    ChatRequestViewModel[] allRequests = [];
    Dictionary<string, string>? requestLabels;
    CancellationTokenSource? operation;
    Task loadTask = Task.CompletedTask;
    string? localAccountId;
    bool isNavigating;
    bool hasLoaded;
    bool isRefreshing;
    bool isSilentRefresh;
    bool isPageVisible;
    bool reloadPending;

    public ChatRequestViewModel[] Requests { get; private set { field = value; OnPropertyChanged(); } } = [];
    bool HasCurrentRequestState => hasLoaded && messagingService.IsEnabled && localAccountId == messagingService.AccountId;

    bool IsLoading => operation is not null;
    public bool ShowLoading => IsLoading && (!isSilentRefresh || !hasLoaded);
    public bool IsChatEnabled { get; private set; }
    public bool CanRetry => isPageVisible && !IsLoading && !isNavigating && ErrorText is not null;
    public bool CanUseRequests => isPageVisible && ReferenceEquals(Shell.Current?.CurrentPage, this)
        && IsChatEnabled && HasCurrentRequestState && (!IsLoading || isRefreshing) && !isNavigating;
    public bool ShowEmpty => hasLoaded && (!IsLoading || isRefreshing);
    public string? ErrorText { get; private set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRetry)); } }
    public string BusyText { get; private set { field = value; OnPropertyChanged(); } } = Strings.Loading;

    public ChatRequestsPage(MessagingService messagingService, AddressBookService addressBookService)
    {
        this.messagingService = messagingService;
        this.addressBookService = addressBookService;
        InitializeComponent();
        requestDirectionTabs.SelectedTabChanged += (_, _) => FilterRequests();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        isPageVisible = true;
        messagingService.Changed += OnMessagingChanged;
        if (!IsLoading) await (loadTask = LoadAsync());
    }

    protected override void OnDisappearing()
    {
        isPageVisible = false;
        reloadPending = false;
        messagingService.Changed -= OnMessagingChanged;
        operation?.Cancel();
        operation = null;
        isRefreshing = false;
        IsChatEnabled = false;
        ClearRequests();
        NotifyLoading();
        base.OnDisappearing();
    }

    async void OnRetryClicked(object sender, EventArgs e)
    {
        if (CanRetry) await (loadTask = LoadAsync());
    }

    void OnMessagingChanged(object? sender, EventArgs e)
    {
        reloadPending = true;
        ReloadIfIdle();
    }

    async void ReloadIfIdle()
    {
        if (!isPageVisible || !reloadPending || IsLoading) return;
        reloadPending = false;
        await (loadTask = LoadAsync(silent: true));
    }

    async void OnRequestTapped(object sender, TappedEventArgs e)
    {
        if (!CanUseRequests || e.Parameter is not ChatRequestViewModel request
            || !allRequests.Contains(request) || request.LocalAccountId != localAccountId) return;
        isNavigating = true;
        NotifyLoading();
        try
        {
            await Shell.Current.GoToAsync("chat-request", new ShellNavigationQueryParameters { ["address"] = request.Address });
        }
        catch (Exception ex)
        {
            if (isPageVisible && request.LocalAccountId == localAccountId && localAccountId == messagingService.AccountId)
                ErrorText = ex.Message;
        }
        finally
        {
            isNavigating = false;
            NotifyLoading();
        }
    }

    async void OnAcceptClicked(object sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: ChatRequestViewModel request }) await RespondAsync(request, true);
    }

    async void OnIgnoreClicked(object sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: ChatRequestViewModel request }) await RespondAsync(request, false);
    }

    async Task RespondAsync(ChatRequestViewModel request, bool accept)
    {
        if (!CanUseRequests || !request.CanRespond || !allRequests.Contains(request) || request.LocalAccountId != localAccountId) return;
        var pendingRefresh = operation;
        Task pendingLoad = loadTask;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        isRefreshing = false;
        isSilentRefresh = false;
        pendingRefresh?.Cancel();
        ErrorText = null;
        BusyText = accept ? Strings.ChatRequestAccepting : Strings.ChatRequestIgnoring;
        NotifyLoading();
        try
        {
            // Give the user's action priority; let the canceled refresh release its SDK and database work first.
            await pendingLoad;
            cancellation.Token.ThrowIfCancellationRequested();
            await messagingService.RespondToContactRequestAsync(request.LocalAccountId, request.Address,
                request.MessageId, request.RequestCreatedAt, accept, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            await Toast.Show(accept ? Strings.ChatRequestAccepted : Strings.ChatRequestIgnored);
        }
        catch (Exception) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { ErrorText = ex.Message; }
        finally
        {
            if (!cancellation.IsCancellationRequested)
            {
                try
                {
                    await ReadRequestsAsync(cancellation.Token);
                }
                catch (Exception) when (cancellation.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    IsChatEnabled = messagingService.IsEnabled;
                    if (!IsChatEnabled || localAccountId != messagingService.AccountId) ClearRequests();
                    ErrorText ??= ex.Message;
                }
            }
            if (ReferenceEquals(operation, cancellation))
            {
                operation = null;
                NotifyLoading();
                ReloadIfIdle();
            }
        }
    }

    async Task LoadAsync(bool silent = false)
    {
        if (IsLoading) return;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        IsChatEnabled = HasCurrentRequestState;
        if (hasLoaded && !IsChatEnabled) ClearRequests();
        isRefreshing = hasLoaded;
        isSilentRefresh = silent;
        ErrorText = null;
        BusyText = Strings.Loading;
        NotifyLoading();
        bool hasChatState = false;
        try
        {
            await messagingService.InitializeAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            hasChatState = true;
            IsChatEnabled = messagingService.IsEnabled;
            if (hasLoaded && localAccountId != messagingService.AccountId) ClearRequests();
            if (!IsChatEnabled)
            {
                ClearRequests();
                ErrorText = Strings.ChatEnableFirst;
                return;
            }
            NotifyLoading();
            await ReadRequestsAsync(cancellation.Token, refreshLabels: !silent);
        }
        catch (Exception) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            IsChatEnabled = messagingService.IsEnabled && (hasChatState || HasCurrentRequestState);
            if (!HasCurrentRequestState) ClearRequests();
            ErrorText ??= ex.Message;
        }
        finally
        {
            if (ReferenceEquals(operation, cancellation))
            {
                operation = null;
                isRefreshing = false;
                NotifyLoading();
                ReloadIfIdle();
            }
        }
    }

    async Task ReadRequestsAsync(CancellationToken cancellationToken, bool refreshLabels = false)
    {
        // Reuse local names during background updates and after responding to a request.
        var labels = refreshLabels ? null : requestLabels;
        labels ??= (await addressBookService.GetEntriesAsync(cancellationToken))
            .ToDictionary(p => p.Address, p => p.Label, StringComparer.Ordinal);
        cancellationToken.ThrowIfCancellationRequested();
        requestLabels = labels;
        var snapshot = await messagingService.GetContactRequestsAsync(cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        localAccountId = snapshot.LocalAccountId;
        var previous = allRequests.ToDictionary(p => (p.LocalAccountId, p.Address, p.IsIncoming));
        allRequests = snapshot.Requests.Select(details =>
        {
            string? displayName = messagingService.GetChatDisplayName(details.LocalAccountId, details.Address,
                labels.GetValueOrDefault(details.Address));
            var current = new ChatRequestViewModel(details, displayName);
            if (previous.TryGetValue((current.LocalAccountId, current.Address, current.IsIncoming), out var existing)
                && existing.MessageId == current.MessageId && existing.RequestCreatedAt == current.RequestCreatedAt
                && existing.Note == current.Note && existing.Status == current.Status && existing.CanRespond == current.CanRespond)
            {
                // A background nickname update must keep the existing accept/ignore button targets.
                existing.UpdateDisplayName(displayName);
                return existing;
            }
            return current;
        }).ToArray();
        hasLoaded = true;
        FilterRequests();
    }

    void ClearRequests()
    {
        localAccountId = null;
        requestLabels = null;
        allRequests = [];
        hasLoaded = false;
        FilterRequests();
        OnPropertyChanged(nameof(ShowEmpty));
    }

    void FilterRequests()
    {
        var requests = allRequests
            .Where(p => p.IsIncoming == (requestDirectionTabs.SelectedIndex == 0))
            .ToArray();
        // Unchanged notifications must not reset the native list or replace its button targets.
        if (!Requests.SequenceEqual(requests)) Requests = requests;
    }

    void NotifyLoading()
    {
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(IsChatEnabled));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanUseRequests));
        OnPropertyChanged(nameof(ShowEmpty));
    }
}
