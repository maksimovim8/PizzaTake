using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;
using PizzaTake.Core.Tests.Helpers;
using PizzaTake.Core.ViewModels;
using Xunit;

namespace PizzaTake.Core.Tests.Unit;

public sealed class QualityReportViewModelTests
{
    private readonly Mock<IQualityReportRepository> reportRepo = new();
    private readonly Mock<IPermissionService> permissionService = new();
    private readonly Mock<IMediaService> mediaService = new();
    private readonly Mock<ILocationService> locationService = new();
    private readonly Mock<ISyncService> syncService = new();
    private readonly Mock<INavigationService> navigation = new();
    private readonly Mock<IDialogService> dialogService = new();
    private readonly ImmediateMainThreadDispatcher dispatcher = new();
    private readonly Mock<ICrashReportingService> crashReporting = new();
    private readonly Mock<ILogger<QualityReportViewModel>> logger = new();

    private QualityReportViewModel CreateSut() => new(
        reportRepo.Object,
        permissionService.Object,
        mediaService.Object,
        locationService.Object,
        syncService.Object,
        navigation.Object,
        dialogService.Object,
        dispatcher,
        crashReporting.Object,
        logger.Object);

    [Fact]
    public async Task TakePhotoCommand_WhenCameraPermissionDenied_ShouldNotCallMediaServiceSetErrorAndLogWarning()
    {
        permissionService
            .Setup(p => p.EnsureGrantedAsync(AppPermission.Camera, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        var sut = CreateSut();
        await sut.TakePhotoCommand.ExecuteAsync(null);

        mediaService.Verify(m => m.CapturePhotoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        sut.HasPhoto.Should().BeFalse();
        sut.HasError.Should().BeTrue();
        sut.ErrorMessage.Should().Contain("камер");

        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Camera permission denied")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task TakePhotoCommand_WhenPermissionGrantedAndCaptureSucceeds_ShouldSetPhotoPathAndStatus()
    {
        permissionService
            .Setup(p => p.EnsureGrantedAsync(AppPermission.Camera, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        mediaService
            .Setup(m => m.CapturePhotoAsync("report", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaCaptureResult("/app/photos/report_20261126.jpg"));

        var sut = CreateSut();
        await sut.TakePhotoCommand.ExecuteAsync(null);

        sut.PhotoPath.Should().Be("/app/photos/report_20261126.jpg");
        sut.HasPhoto.Should().BeTrue();
        sut.HasError.Should().BeFalse();
        sut.HasStatusMessage.Should().BeTrue();
    }

    [Fact]
    public async Task TakePhotoCommand_WhenCaptureCancelled_ShouldSetStatusWithoutError()
    {
        permissionService
            .Setup(p => p.EnsureGrantedAsync(AppPermission.Camera, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        mediaService
            .Setup(m => m.CapturePhotoAsync("report", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaCaptureResult(null, HardwareFailureReason.Cancelled, "Зйомку фото скасовано."));

        var sut = CreateSut();
        await sut.TakePhotoCommand.ExecuteAsync(null);

        sut.HasPhoto.Should().BeFalse();
        sut.HasError.Should().BeFalse();
        sut.StatusMessage.Should().Contain("скасовано");
    }

    [Fact]
    public async Task PickPhotoCommand_WhenGallerySucceeds_ShouldSetPhotoPath()
    {
        mediaService
            .Setup(m => m.PickPhotoAsync("report", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaCaptureResult("/app/photos/gallery_01.jpg"));

        var sut = CreateSut();
        await sut.PickPhotoCommand.ExecuteAsync(null);

        sut.PhotoPath.Should().Be("/app/photos/gallery_01.jpg");
        sut.HasPhoto.Should().BeTrue();
        sut.HasStatusMessage.Should().BeTrue();
    }

    [Fact]
    public async Task GetLocationCommand_WhenGpsFeatureNotEnabled_ShouldSetErrorWithoutExceptionAndLogWarning()
    {
        permissionService
            .Setup(p => p.EnsureGrantedAsync(AppPermission.LocationWhenInUse, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        locationService
            .Setup(l => l.GetCurrentLocationAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationResult(null, HardwareFailureReason.FeatureNotEnabled, "GPS вимкнено на пристрої."));

        var sut = CreateSut();
        await sut.GetLocationCommand.ExecuteAsync(null);

        sut.HasCoordinates.Should().BeFalse();
        sut.HasError.Should().BeTrue();
        sut.ErrorMessage.Should().Be("GPS вимкнено на пристрої.");
        sut.StatusMessage.Should().BeEmpty();

        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("FeatureNotEnabled")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetLocationCommand_WhenPermissionDenied_ShouldNotCallLocationService()
    {
        permissionService
            .Setup(p => p.EnsureGrantedAsync(AppPermission.LocationWhenInUse, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        var sut = CreateSut();
        await sut.GetLocationCommand.ExecuteAsync(null);

        locationService.Verify(l => l.GetCurrentLocationAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
        sut.HasError.Should().BeTrue();
    }

    [Fact]
    public async Task SaveReportCommand_WithValidDescriptionPhotoAndLocation_ShouldPersistSyncTrackAndResetForm()
    {
        permissionService
            .Setup(p => p.EnsureGrantedAsync(AppPermission.LocationWhenInUse, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        locationService
            .Setup(l => l.GetCurrentLocationAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationResult(new GeoPoint(50.4501, 30.5234)));
        mediaService
            .Setup(m => m.PickPhotoAsync("report", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaCaptureResult("/app/photos/cold_pizza.jpg"));
        reportRepo
            .Setup(r => r.SaveAsync(It.IsAny<QualityReport>()))
            .Callback<QualityReport>(r => r.Id = 99)
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        sut.ApplyQueryAttributes(new Dictionary<string, object>
        {
            ["Order"] = new Order { Id = 12, OrderNumber = "PD-1212" }
        });

        await sut.PickPhotoCommand.ExecuteAsync(null);
        await sut.GetLocationCommand.ExecuteAsync(null);
        sut.Description = "Піца приїхала теплою, але коробка пом'ята";

        sut.HasOrderContext.Should().BeTrue();
        sut.OrderContextText.Should().Contain("PD-1212");
        sut.CoordinatesText.Should().Contain("50.45010");
        sut.SaveReportCommand.CanExecute(null).Should().BeTrue();

        await sut.SaveReportCommand.ExecuteAsync(null);

        reportRepo.Verify(r => r.SaveAsync(It.Is<QualityReport>(q =>
            q.OrderId == 12 &&
            q.OrderNumber == "PD-1212" &&
            q.PhotoPath == "/app/photos/cold_pizza.jpg" &&
            q.Latitude == 50.4501 &&
            q.Longitude == 30.5234)), Times.Once);

        syncService.Verify(s => s.EnqueueAsync("QualityReport", "Insert", It.IsAny<object>()), Times.Once);
        crashReporting.Verify(c => c.TrackEvent(CrashEventNames.QualityReportCreated, It.IsAny<IDictionary<string, string>>()), Times.Once);

        sut.SavedReports.Should().ContainSingle(r => r.Id == 99);
        sut.HasSavedReports.Should().BeTrue();
        sut.Description.Should().BeEmpty();
        sut.HasPhoto.Should().BeFalse();
        sut.HasCoordinates.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteReportCommand_WhenConfirmed_ShouldDeleteEnqueueSyncAndRemoveItem()
    {
        var report = new QualityReport { Id = 7, Description = "Старе звернення" };
        reportRepo.Setup(r => r.GetAllAsync()).ReturnsAsync([report]);
        dialogService
            .Setup(d => d.ConfirmAsync("Видалення звернення", It.IsAny<string>(), "Видалити", "Скасувати"))
            .ReturnsAsync(true);

        var sut = CreateSut();
        await sut.LoadReportsCommand.ExecuteAsync(null);
        sut.SavedReports.Should().HaveCount(1);

        await sut.DeleteReportCommand.ExecuteAsync(report);

        reportRepo.Verify(r => r.DeleteAsync(report), Times.Once);
        syncService.Verify(s => s.EnqueueAsync("QualityReport", "Delete", It.IsAny<object>()), Times.Once);
        sut.SavedReports.Should().BeEmpty();
        sut.HasNoSavedReports.Should().BeTrue();
    }
}
