using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using PizzaTake.Models;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

/// <summary>
/// ViewModel for inspecting, filtering, and reporting local Serilog diagnostic logs.
/// </summary>
public sealed class LogsViewModel : BaseViewModel
{
    public const int DefaultMaxEntries = 100;
    public const int BugReportLogLinesCount = 30;

    private readonly ILogReaderService logReaderService;
    private readonly ICrashReportingService crashReportingService;
    private readonly ILogger<LogsViewModel> logger;

    private string selectedLevel = "Усі";
    private string bugReportComment = string.Empty;
    private string statusMessage = string.Empty;
    private string logFileInfo = string.Empty;

    public LogsViewModel(
        ILogReaderService logReaderService,
        ICrashReportingService crashReportingService,
        ILogger<LogsViewModel> logger)
    {
        this.logReaderService = logReaderService;
        this.crashReportingService = crashReportingService;
        this.logger = logger;
        Title = "Журнал логів";

        LoadLogsCommand = new Command(async () => await LoadLogsAsync());
        SelectLevelCommand = new Command<string?>(async level => await SelectLevelAsync(level));
        SendBugReportCommand = new Command(async () => await SendBugReportAsync(), () => !IsBusy);
        TestTaskExceptionCommand = new Command(async () => await TriggerUnobservedTaskExceptionAsync(), () => !IsBusy);
        TestSentryCrashCommand = new Command(async () => await TriggerTestCrashAsync());
        GoBackCommand = new Command(async () => await Shell.Current.GoToAsync(".."));
    }

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public IReadOnlyList<string> LevelFilters { get; } =
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

    public ICommand LoadLogsCommand { get; }
    public ICommand SelectLevelCommand { get; }
    public ICommand SendBugReportCommand { get; }
    public ICommand TestTaskExceptionCommand { get; }
    public ICommand TestSentryCrashCommand { get; }
    public ICommand GoBackCommand { get; }

    public async Task LoadLogsAsync()
    {
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
            var recentLines = await logReaderService.ReadRecentRawLinesAsync(BugReportLogLinesCount);
            var sentToSentry = await crashReportingService.SendUserBugReportAsync(BugReportComment, recentLines);
            BugReportComment = string.Empty;

            if (sentToSentry)
            {
                StatusMessage = $"Звіт про помилку надіслано в Sentry (додано {recentLines.Count} рядків логу).";
                await Shell.Current.DisplayAlertAsync("Звіт надіслано", StatusMessage, "OK");
            }
            else
            {
                StatusMessage = $"Звіт зафіксовано у локальному журналі ({recentLines.Count} рядків). Налаштуйте SENTRY_DSN для надсилання у Sentry.";
                await Shell.Current.DisplayAlertAsync("Звіт збережено локально", StatusMessage, "OK");
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
        var confirmed = await Shell.Current.DisplayAlertAsync(
            "Тест крашу (Sentry)",
            "Це спричинить навмисне аварійне завершення застосунку для перевірки панелі Sentry Issues. Продовжити?",
            "Викликати краш",
            "Скасувати");

        if (!confirmed)
            return;

        crashReportingService.TriggerTestCrash();
    }

    private void UpdateCommandStates()
    {
        ((Command)SendBugReportCommand).ChangeCanExecute();
        ((Command)TestTaskExceptionCommand).ChangeCanExecute();
    }
}
