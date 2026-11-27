using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.ViewModels;

/// <summary>
/// ViewModel for inspecting, filtering, and reporting local Serilog diagnostic logs.
/// </summary>
public sealed class LogsViewModel : BaseViewModel
{
    public const int DefaultMaxEntries = 100;
    public const int BugReportLogLinesCount = 30;

    private readonly ILogReaderService logReaderService;
    private readonly ICrashReportingService crashReportingService;
    private readonly ISessionService sessionService;
    private readonly INavigationService navigation;
    private readonly IDialogService dialogService;
    private readonly IBugReportService? bugReportService;
    private readonly ILogger<LogsViewModel> logger;

    private string selectedLevel = "Усі";
    private string bugReportComment = string.Empty;
    private string statusMessage = string.Empty;
    private string logFileInfo = string.Empty;

    public LogsViewModel(
        ILogReaderService logReaderService,
        ICrashReportingService crashReportingService,
        ISessionService sessionService,
        INavigationService navigation,
        IDialogService dialogService,
        ILogger<LogsViewModel>? logger = null,
        IBugReportService? bugReportService = null)
    {
        this.logReaderService = logReaderService;
        this.crashReportingService = crashReportingService;
        this.sessionService = sessionService;
        this.navigation = navigation;
        this.dialogService = dialogService;
        this.bugReportService = bugReportService;
        this.logger = logger ?? NullLogger<LogsViewModel>.Instance;
        Title = "Журнал логів";

        LoadLogsCommand = new AsyncRelayCommand(LoadLogsAsync);
        SelectLevelCommand = new AsyncRelayCommand<string?>(SelectLevelAsync);
        SendBugReportCommand = new AsyncRelayCommand(SendBugReportAsync, () => !IsBusy);
        TestTaskExceptionCommand = new AsyncRelayCommand(TriggerUnobservedTaskExceptionAsync, () => !IsBusy);
        TestSentryCrashCommand = new AsyncRelayCommand(TriggerTestCrashAsync);
        GoBackCommand = new AsyncRelayCommand(() => navigation.GoBackAsync());
    }

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public string[] LevelFilters { get; } =
    [
        "Усі",
        "Information",
        "Warning",
        "Error",
        "Fatal"
    ];

    public string SelectedLevel
    {
        get => selectedLevel;
        set
        {
            if (SetProperty(ref selectedLevel, value))
            {
                OnPropertyChanged(nameof(IsAllSelected));
                OnPropertyChanged(nameof(IsInfoSelected));
                OnPropertyChanged(nameof(IsWarningSelected));
                OnPropertyChanged(nameof(IsErrorSelected));
                OnPropertyChanged(nameof(IsFatalSelected));
            }
        }
    }

    public bool IsAllSelected => string.Equals(SelectedLevel, "Усі", StringComparison.OrdinalIgnoreCase);
    public bool IsInfoSelected => string.Equals(SelectedLevel, "Information", StringComparison.OrdinalIgnoreCase);
    public bool IsWarningSelected => string.Equals(SelectedLevel, "Warning", StringComparison.OrdinalIgnoreCase);
    public bool IsErrorSelected => string.Equals(SelectedLevel, "Error", StringComparison.OrdinalIgnoreCase);
    public bool IsFatalSelected => string.Equals(SelectedLevel, "Fatal", StringComparison.OrdinalIgnoreCase);

    public string BugReportComment
    {
        get => bugReportComment;
        set => SetProperty(ref bugReportComment, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (SetProperty(ref statusMessage, value))
                OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public string LogFileInfo
    {
        get => logFileInfo;
        private set => SetProperty(ref logFileInfo, value);
    }

    public bool IsEmpty => LogEntries.Count == 0;

    public IAsyncRelayCommand LoadLogsCommand { get; }
    public IAsyncRelayCommand<string?> SelectLevelCommand { get; }
    public IAsyncRelayCommand SendBugReportCommand { get; }
    public IAsyncRelayCommand TestTaskExceptionCommand { get; }
    public IAsyncRelayCommand TestSentryCrashCommand { get; }
    public IAsyncRelayCommand GoBackCommand { get; }

    public async Task LoadLogsAsync()
    {
        if (!await EnsureAdminAccessAsync(nameof(LoadLogsCommand), navigateBackOnDeny: true))
            return;

        if (IsBusy)
            return;

        IsBusy = true;
        UpdateCommandStates();
        try
        {
            var entries = await logReaderService.ReadRecentEntriesAsync(DefaultMaxEntries, SelectedLevel);
            LogEntries.Clear();
            foreach (var entry in entries)
                LogEntries.Add(entry);

            var filePath = logReaderService.GetLatestLogFilePath();
            var fileName = string.IsNullOrEmpty(filePath) ? "файл ще не створено" : Path.GetFileName(filePath);
            LogFileInfo = $"Файл: {fileName} · Показано: {LogEntries.Count} (фільтр: {SelectedLevel})";
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load log entries for LogsPage");
            StatusMessage = "Не вдалося прочитати файл журналу.";
        }
        finally
        {
            IsBusy = false;
            UpdateCommandStates();
        }
    }

    public async Task SelectLevelAsync(string? level)
    {
        if (string.IsNullOrWhiteSpace(level))
            return;

        SelectedLevel = level;
        await LoadLogsAsync();
    }

    private async Task SendBugReportAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        UpdateCommandStates();
        try
        {
            if (bugReportService is not null)
            {
                var result = await bugReportService.SendReportAsync(BugReportComment, "LogsPage");
                BugReportComment = string.Empty;
                StatusMessage = result.Message;
                await dialogService.AlertAsync(
                    result.SentToSentry ? "Звіт надіслано" : "Звіт збережено локально",
                    StatusMessage);
            }
            else
            {
                var recentLines = await logReaderService.ReadRecentRawLinesAsync(BugReportLogLinesCount);
                var sentToSentry = await crashReportingService.SendUserBugReportAsync(BugReportComment, recentLines);
                BugReportComment = string.Empty;

                if (sentToSentry)
                {
                    StatusMessage = $"Звіт про помилку надіслано в Sentry (додано {recentLines.Count} рядків логу).";
                    await dialogService.AlertAsync("Звіт надіслано", StatusMessage);
                }
                else
                {
                    StatusMessage = $"Звіт зафіксовано у локальному журналі ({recentLines.Count} рядків). Налаштуйте SENTRY_DSN для надсилання у Sentry.";
                    await dialogService.AlertAsync("Звіт збережено локально", StatusMessage);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send bug report from LogsPage");
            StatusMessage = "Помилка під час формування звіту.";
        }
        finally
        {
            IsBusy = false;
            UpdateCommandStates();
        }

        await LoadLogsAsync();
    }

    private async Task TriggerUnobservedTaskExceptionAsync()
    {
        if (!await EnsureAdminAccessAsync(nameof(TestTaskExceptionCommand), navigateBackOnDeny: false))
            return;

        if (IsBusy)
            return;

        IsBusy = true;
        UpdateCommandStates();
        try
        {
            logger.LogInformation("Simulating unobserved background Task exception from LogsPage");
            SpawnFaultedBackgroundTask();

            await Task.Delay(200);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(250);

            StatusMessage = "Фоновий виняток змодельовано та перехоплено без падіння застосунку.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while simulating unobserved Task exception");
        }
        finally
        {
            IsBusy = false;
            UpdateCommandStates();
        }

        await LoadLogsAsync();
    }

    private static void SpawnFaultedBackgroundTask()
    {
        _ = Task.Run(() => throw new InvalidOperationException("Simulated unobserved background task exception (Lab 5 test)."));
    }

    private async Task TriggerTestCrashAsync()
    {
        if (!await EnsureAdminAccessAsync(nameof(TestSentryCrashCommand), navigateBackOnDeny: false))
            return;

        var confirmed = await dialogService.ConfirmAsync(
            "Тест крашу (Sentry)",
            "Це спричинить навмисне аварійне завершення застосунку для перевірки панелі Sentry Issues. Продовжити?",
            "Викликати краш",
            "Скасувати");

        if (!confirmed)
            return;

        crashReportingService.TriggerTestCrash();
    }

    private async Task<bool> EnsureAdminAccessAsync(string commandName, bool navigateBackOnDeny)
    {
        if (sessionService.IsInRole(UserRole.Administrator))
            return true;

        logger.LogWarning("Access denied for role {Role} on {Command}", sessionService.CurrentRole, commandName);
        LogEntries.Clear();
        OnPropertyChanged(nameof(IsEmpty));
        await dialogService.AlertAsync("Доступ заборонено", "Дія доступна лише адміністратору.");
        if (navigateBackOnDeny)
            await navigation.GoBackAsync();
        return false;
    }

    private void UpdateCommandStates()
    {
        SendBugReportCommand.NotifyCanExecuteChanged();
        TestTaskExceptionCommand.NotifyCanExecuteChanged();
    }
}
