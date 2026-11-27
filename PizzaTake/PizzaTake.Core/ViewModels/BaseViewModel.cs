using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PizzaTake.Core.Models;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.ViewModels;

public abstract class BaseViewModel : ObservableObject
{
    private string title = string.Empty;
    private bool isBusy;

    public string Title
    {
        get => title;
        set => SetProperty(ref title, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        set => SetProperty(ref isBusy, value);
    }

    protected static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }

    protected static async Task<bool> RequireAdminAsync(
        ISessionService session,
        IDialogService dialog,
        ILogger logger,
        string commandName)
    {
        if (session.IsInRole(UserRole.Administrator))
            return true;

        logger.LogWarning("Access denied for role {Role} on {Command}", session.CurrentRole, commandName);
        await dialog.AlertAsync("Доступ заборонено", "Дія доступна лише адміністратору.");
        return false;
    }

    protected static bool TryParseDecimal(string? raw, out decimal result)
    {
        result = 0m;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var normalized = raw.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    protected static bool TryParseNonNegativeDecimal(string? raw, out decimal result)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            result = 0m;
            return true;
        }

        return TryParseDecimal(raw, out result) && result >= 0m;
    }
}

/// <summary>
/// Shared base for ViewModels that display an offline status banner and pending sync queue counter.
/// </summary>
public abstract class ConnectivityBannerViewModel : BaseViewModel
{
    private readonly IConnectivityService? connectivityService;
    private readonly ISyncService? syncService;
    private readonly IMainThreadDispatcher? dispatcher;
    private readonly ILogger logger;

    private bool isOffline;
    private int pendingSyncCount;

    protected ConnectivityBannerViewModel(
        IConnectivityService? connectivityService,
        ISyncService? syncService,
        IMainThreadDispatcher? dispatcher,
        ILogger logger)
    {
        this.connectivityService = connectivityService;
        this.syncService = syncService;
        this.dispatcher = dispatcher;
        this.logger = logger;
    }

    public bool IsOffline
    {
        get => isOffline;
        protected set
        {
            if (SetProperty(ref isOffline, value))
                OnPropertyChanged(nameof(OfflineBannerText));
        }
    }

    public int PendingSyncCount
    {
        get => pendingSyncCount;
        protected set
        {
            if (SetProperty(ref pendingSyncCount, value))
                OnPropertyChanged(nameof(OfflineBannerText));
        }
    }

    public virtual string OfflineBannerText => $"Офлайн-режим (у черзі: {PendingSyncCount})";

    public void StartConnectivityMonitoring()
    {
        if (connectivityService is not null)
        {
            connectivityService.StartMonitoring();
            IsOffline = !connectivityService.IsConnected;
            connectivityService.ConnectivityChanged -= OnConnectivityChanged;
            connectivityService.ConnectivityChanged += OnConnectivityChanged;
        }

        if (syncService is not null)
        {
            syncService.SyncCompleted -= OnSyncCompleted;
            syncService.SyncCompleted += OnSyncCompleted;
        }

        _ = RefreshPendingSyncCountAsync();
    }

    public void StopConnectivityMonitoring()
    {
        if (connectivityService is not null)
            connectivityService.ConnectivityChanged -= OnConnectivityChanged;

        if (syncService is not null)
            syncService.SyncCompleted -= OnSyncCompleted;
    }

    private void OnConnectivityChanged(object? sender, bool isConnected)
    {
        Dispatch(() => IsOffline = !isConnected);
        _ = RefreshPendingSyncCountAsync();
    }

    private void OnSyncCompleted(object? sender, EventArgs e) => _ = RefreshPendingSyncCountAsync();

    protected async Task RefreshPendingSyncCountAsync()
    {
        if (syncService is null)
            return;

        try
        {
            var count = await syncService.GetPendingCountAsync();
            Dispatch(() => PendingSyncCount = count);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to refresh pending sync count in {ViewModel}", GetType().Name);
        }
    }

    private void Dispatch(Action action)
    {
        if (dispatcher is not null)
            dispatcher.BeginInvokeOnMainThread(action);
        else
            action();
    }
}

/// <summary>
/// Shared base for form ViewModels supporting error/status banners and optional user bug-report submission.
/// </summary>
public abstract class FormFeedbackViewModel : BaseViewModel
{
    private readonly IBugReportService? bugReportService;
    private readonly string defaultReportContext;

    private string statusMessage = string.Empty;
    private string errorMessage = string.Empty;
    private string? reportableErrorContext;

    protected FormFeedbackViewModel(IBugReportService? bugReportService = null, string defaultReportContext = "Форма")
    {
        this.bugReportService = bugReportService;
        this.defaultReportContext = defaultReportContext;
        ReportErrorCommand = new AsyncRelayCommand(ReportErrorAsync, () => CanReportError);
    }

    public string StatusMessage
    {
        get => statusMessage;
        protected set
        {
            if (SetProperty(ref statusMessage, value))
                OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public string ErrorMessage
    {
        get => errorMessage;
        protected set
        {
            if (SetProperty(ref errorMessage, value))
            {
                if (string.IsNullOrWhiteSpace(value))
                    SetReportableErrorContext(null);
                OnPropertyChanged(nameof(HasError));
                OnErrorMessageChanged();
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool CanReportError => HasError && !string.IsNullOrEmpty(reportableErrorContext) && bugReportService is not null;
    public IAsyncRelayCommand ReportErrorCommand { get; }

    protected virtual void OnErrorMessageChanged() { }

    protected bool SetInput(ref string field, string value, IRelayCommand? command = null, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
            return false;

        ErrorMessage = string.Empty;
        command?.NotifyCanExecuteChanged();
        return true;
    }

    protected void SetReportableErrorContext(string? context)
    {
        reportableErrorContext = context;
        OnPropertyChanged(nameof(CanReportError));
        OnErrorMessageChanged();
        ReportErrorCommand.NotifyCanExecuteChanged();
    }

    private Task ReportErrorAsync() =>
        bugReportService is null
            ? Task.CompletedTask
            : bugReportService.PromptAndSendAsync(reportableErrorContext ?? defaultReportContext);
}
