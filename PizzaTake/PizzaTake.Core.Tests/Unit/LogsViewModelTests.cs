using FluentAssertions;
using Moq;
using PizzaTake.Core.Models;
using PizzaTake.Core.Services;
using PizzaTake.Core.ViewModels;
using Xunit;

namespace PizzaTake.Core.Tests.Unit;

public sealed class LogsViewModelTests
{
    private readonly Mock<ILogReaderService> logReader = new();
    private readonly Mock<ICrashReportingService> crashReporting = new();
    private readonly Mock<ISessionService> sessionService = new();
    private readonly Mock<INavigationService> navigation = new();
    private readonly Mock<IDialogService> dialogService = new();

    private LogsViewModel CreateSut() => new(
        logReader.Object,
        crashReporting.Object,
        sessionService.Object,
        navigation.Object,
        dialogService.Object);

    [Fact]
    public async Task SelectLevelCommand_WithErrorFilter_ShouldLoadOnlyErrorLogs()
    {
        sessionService.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(true);
        var errorLogs = new List<LogEntry>
        {
            new() { Timestamp = "2026-11-26 12:00:00.000", Level = "Error", LevelShort = "ERR", Source = "CheckoutViewModel", Message = "Order save failed" }
        };
        logReader.Setup(r => r.ReadRecentEntriesAsync(LogsViewModel.DefaultMaxEntries, "Error", default))
            .ReturnsAsync(errorLogs);
        logReader.Setup(r => r.GetLatestLogFilePath()).Returns("/logs/app-20261126.log");

        var sut = CreateSut();
        await sut.SelectLevelCommand.ExecuteAsync("Error");

        sut.SelectedLevel.Should().Be("Error");
        sut.IsErrorSelected.Should().BeTrue();
        sut.LogEntries.Should().ContainSingle(e => e.Level == "Error" && e.Message == "Order save failed");
        sut.LogFileInfo.Should().Contain("app-20261126.log").And.Contain("Error");
    }

    [Fact]
    public async Task LoadLogsCommand_WhenNotAdministrator_ShouldDenyAndNavigateBack()
    {
        sessionService.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(false);
        sessionService.Setup(s => s.CurrentRole).Returns(UserRole.Customer);

        var sut = CreateSut();
        await sut.LoadLogsCommand.ExecuteAsync(null);

        sut.LogEntries.Should().BeEmpty();
        logReader.Verify(r => r.ReadRecentEntriesAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        dialogService.Verify(d => d.AlertAsync("Доступ заборонено", It.IsAny<string>(), "OK"), Times.Once);
        navigation.Verify(n => n.GoBackAsync(null), Times.Once);
    }

    [Fact]
    public async Task SendBugReportCommand_WhenNotAdministrator_ShouldDenyWithoutReadingLogsOrReporting()
    {
        sessionService.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(false);
        sessionService.Setup(s => s.CurrentRole).Returns(UserRole.Customer);
        var bugReportService = new Mock<IBugReportService>();

        var sut = new LogsViewModel(
            logReader.Object,
            crashReporting.Object,
            sessionService.Object,
            navigation.Object,
            dialogService.Object,
            bugReportService: bugReportService.Object);
        sut.BugReportComment = "Test issue";

        await sut.SendBugReportCommand.ExecuteAsync(null);

        dialogService.Verify(d => d.AlertAsync("Доступ заборонено", It.IsAny<string>(), "OK"), Times.Once);
        logReader.Verify(r => r.ReadRecentRawLinesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        logReader.Verify(r => r.ReadRecentEntriesAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        crashReporting.Verify(c => c.SendUserBugReportAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        bugReportService.Verify(b => b.SendReportAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendBugReportCommand_WithoutBugReportService_WhenNotAdministrator_ShouldDenyWithoutReadingLogsOrReporting()
    {
        sessionService.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(false);
        sessionService.Setup(s => s.CurrentRole).Returns(UserRole.Customer);

        var sut = CreateSut();
        await sut.SendBugReportCommand.ExecuteAsync(null);

        dialogService.Verify(d => d.AlertAsync("Доступ заборонено", It.IsAny<string>(), "OK"), Times.Once);
        logReader.Verify(r => r.ReadRecentRawLinesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        crashReporting.Verify(c => c.SendUserBugReportAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SelectLevelCommand_WhenNotAdministrator_ShouldDenyWithoutMutatingSelectedLevelOrReadingLogs()
    {
        sessionService.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(false);
        sessionService.Setup(s => s.CurrentRole).Returns(UserRole.Customer);

        var sut = CreateSut();
        await sut.SelectLevelCommand.ExecuteAsync("Error");

        sut.SelectedLevel.Should().Be("Усі");
        sut.IsAllSelected.Should().BeTrue();
        sut.IsErrorSelected.Should().BeFalse();
        dialogService.Verify(d => d.AlertAsync("Доступ заборонено", It.IsAny<string>(), "OK"), Times.Once);
        logReader.Verify(r => r.ReadRecentEntriesAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void IsAdmin_WhenSessionChanges_ShouldReflectRoleAndRaisePropertyChanged()
    {
        var isAdmin = false;
        sessionService.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(() => isAdmin);
        var dispatcher = new Mock<IMainThreadDispatcher>();
        dispatcher.Setup(d => d.BeginInvokeOnMainThread(It.IsAny<Action>())).Callback<Action>(a => a());

        var sut = new LogsViewModel(
            logReader.Object,
            crashReporting.Object,
            sessionService.Object,
            navigation.Object,
            dialogService.Object,
            dispatcher: dispatcher.Object);

        var changedProperties = new List<string?>();
        sut.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        sut.StartSessionMonitoring();
        changedProperties.Clear();
        sut.IsAdmin.Should().BeFalse();

        isAdmin = true;
        sessionService.Raise(s => s.SessionChanged += null, EventArgs.Empty);

        sut.IsAdmin.Should().BeTrue();
        changedProperties.Should().Contain(nameof(LogsViewModel.IsAdmin));
        dispatcher.Verify(d => d.BeginInvokeOnMainThread(It.IsAny<Action>()), Times.Once);

        sut.StopSessionMonitoring();
        changedProperties.Clear();
        isAdmin = false;
        sessionService.Raise(s => s.SessionChanged += null, EventArgs.Empty);

        changedProperties.Should().NotContain(nameof(LogsViewModel.IsAdmin));
        dispatcher.Verify(d => d.BeginInvokeOnMainThread(It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public async Task SelectLevelCommand_WithDebugFilter_ShouldMarkDebugSelectedAndLoadDebugLogs()
    {
        sessionService.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(true);
        logReader.Setup(r => r.ReadRecentEntriesAsync(LogsViewModel.DefaultMaxEntries, "Debug", default))
            .ReturnsAsync([new LogEntry { Level = "Debug", LevelShort = "DBG", Message = "Trace step" }]);
        logReader.Setup(r => r.GetLatestLogFilePath()).Returns("/logs/app-20261128.log");

        var sut = CreateSut();
        sut.LevelFilters.Should().Contain("Debug");

        await sut.SelectLevelCommand.ExecuteAsync("Debug");

        sut.SelectedLevel.Should().Be("Debug");
        sut.IsDebugSelected.Should().BeTrue();
        sut.LogEntries.Should().ContainSingle(e => e.Level == "Debug");
    }

    [Fact]
    public async Task TestSslPinningCommand_WhenAdministrator_ShouldRejectSpoofedCertAndReloadLogs()
    {
        sessionService.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(true);
        logReader.Setup(r => r.ReadRecentEntriesAsync(LogsViewModel.DefaultMaxEntries, "Усі", default))
            .ReturnsAsync([]);
        logReader.Setup(r => r.GetLatestLogFilePath()).Returns("/logs/app-20261128.log");

        var sut = CreateSut();
        await sut.TestSslPinningCommand.ExecuteAsync(null);

        sut.StatusMessage.Should().Contain("SSL Pinning активний");
        logReader.Verify(r => r.ReadRecentEntriesAsync(LogsViewModel.DefaultMaxEntries, "Усі", default), Times.Once);
    }

    [Fact]
    public async Task TestSslPinningCommand_WhenNotAdministrator_ShouldDenyAccess()
    {
        sessionService.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(false);
        sessionService.Setup(s => s.CurrentRole).Returns(UserRole.Customer);

        var sut = CreateSut();
        await sut.TestSslPinningCommand.ExecuteAsync(null);

        dialogService.Verify(d => d.AlertAsync("Доступ заборонено", It.IsAny<string>(), "OK"), Times.Once);
        logReader.Verify(r => r.ReadRecentEntriesAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
