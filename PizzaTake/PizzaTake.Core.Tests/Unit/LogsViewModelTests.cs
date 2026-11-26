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
}
