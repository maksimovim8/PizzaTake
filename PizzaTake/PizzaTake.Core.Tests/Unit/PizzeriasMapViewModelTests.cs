using FluentAssertions;
using Moq;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;
using PizzaTake.Core.Tests.Helpers;
using PizzaTake.Core.ViewModels;
using Xunit;

namespace PizzaTake.Core.Tests.Unit;

public sealed class PizzeriasMapViewModelTests
{
    private readonly Mock<IPizzeriaRepository> pizzeriaRepo = new();
    private readonly Mock<IQualityReportRepository> reportRepo = new();
    private readonly Mock<IAppStateService> appState = new();
    private readonly Mock<IMapLauncherService> mapLauncher = new();
    private readonly Mock<INavigationService> navigation = new();
    private readonly ImmediateMainThreadDispatcher dispatcher = new();
    private readonly Mock<ILocationService> locationService = new();
    private readonly Mock<IPermissionService> permissionService = new();

    private PizzeriasMapViewModel CreateSut() => new(
        pizzeriaRepo.Object,
        reportRepo.Object,
        appState.Object,
        mapLauncher.Object,
        navigation.Object,
        dispatcher,
        locationService.Object,
        permissionService.Object);

    [Fact]
    public async Task LoadPinsCommand_ShouldLoadCityPizzeriasAndCoordinateReportsAndSelectCurrentPizzeria()
    {
        var kyiv = new City { Id = 1, Name = "Київ" };
        var activePizzeria = new Pizzeria
        {
            Id = 2,
            CityId = 1,
            StoreNumber = "TT 84",
            Address = "просп. Перемоги, 45",
            Latitude = 50.4500,
            Longitude = 30.4500
        };
        appState.Setup(s => s.SelectedCity).Returns(kyiv);
        appState.Setup(s => s.SelectedPizzeria).Returns(activePizzeria);

        pizzeriaRepo.Setup(r => r.GetByCityIdAsync(1)).ReturnsAsync(
        [
            new Pizzeria { Id = 1, CityId = 1, StoreNumber = "TT 78", Address = "вул. Хрещатик, 22", Latitude = 50.4477, Longitude = 30.5228 },
            activePizzeria,
            new Pizzeria { Id = 3, CityId = 1, StoreNumber = "TT 00", Address = "Без координат", Latitude = 0, Longitude = 0 }
        ]);
        reportRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(
        [
            new QualityReport { Id = 5, OrderNumber = "PD-5555", Description = "Довга доставка", Latitude = 50.4510, Longitude = 30.5200 },
            new QualityReport { Id = 6, Description = "Без GPS", Latitude = null, Longitude = null }
        ]);

        var sut = CreateSut();
        var pinsUpdatedCount = 0;
        sut.PinsUpdated += (_, _) => pinsUpdatedCount++;

        await sut.LoadPinsCommand.ExecuteAsync(null);

        sut.Pins.Should().HaveCount(3);
        sut.Pins.Should().Contain(p => p.Category == "Pizzeria" && p.EntityId == 2);
        sut.Pins.Should().Contain(p => p.Category == "QualityReport" && p.EntityId == 5);
        sut.SelectedPin.Should().NotBeNull();
        sut.SelectedPin!.EntityId.Should().Be(2);
        sut.HasSelectedPin.Should().BeTrue();
        sut.SelectedPinCoordinatesText.Should().Be("50.45000, 30.45000");
        sut.HeaderSubtitle.Should().Contain("Київ").And.Contain("3 маркери");
        pinsUpdatedCount.Should().Be(1);
    }

    [Fact]
    public async Task OpenExternalMapCommand_ShouldInvokeLauncherAndSetStatusWhenUnavailable()
    {
        var pin = new MapPinItem(1, "Піцерія TT 78", "вул. Хрещатик, 22", 50.4477, 30.5228, "Pizzeria");
        pin.CategoryDisplayName.Should().Be("Піцерія");

        var sut = CreateSut();
        sut.OpenExternalMapCommand.CanExecute(null).Should().BeFalse();

        GeoPoint? focused = null;
        sut.FocusRequested += (_, pt) => focused = pt;

        sut.SelectPinCommand.Execute(pin);
        focused.Should().Be(new GeoPoint(50.4477, 30.5228));
        sut.OpenExternalMapCommand.CanExecute(null).Should().BeTrue();

        mapLauncher.Setup(m => m.OpenExternalMapAsync(50.4477, 30.5228, "Піцерія TT 78")).ReturnsAsync(false);
        await sut.OpenExternalMapCommand.ExecuteAsync(null);

        mapLauncher.Verify(m => m.OpenExternalMapAsync(50.4477, 30.5228, "Піцерія TT 78"), Times.Once);
        sut.HasStatusMessage.Should().BeTrue();
    }

    [Fact]
    public async Task CenterOnMyLocationCommand_WhenGrantedAndLocationDetected_ShouldUpdateUserLocationAndFocus()
    {
        permissionService
            .Setup(p => p.EnsureGrantedAsync(AppPermission.LocationWhenInUse, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        locationService
            .Setup(l => l.GetCurrentLocationAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationResult(new GeoPoint(50.4547, 30.5238)));

        var sut = CreateSut();
        GeoPoint? focusPoint = null;
        sut.FocusRequested += (_, pt) => focusPoint = pt;

        await sut.CenterOnMyLocationCommand.ExecuteAsync(null);

        sut.UserLocation.Should().Be(new GeoPoint(50.4547, 30.5238));
        focusPoint.Should().Be(new GeoPoint(50.4547, 30.5238));
        sut.StatusMessage.Should().Contain("50.45470");
    }

    [Fact]
    public async Task CenterOnMyLocationCommand_WhenPermissionDenied_ShouldNotQueryLocation()
    {
        permissionService
            .Setup(p => p.EnsureGrantedAsync(AppPermission.LocationWhenInUse, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        var sut = CreateSut();
        await sut.CenterOnMyLocationCommand.ExecuteAsync(null);

        locationService.Verify(l => l.GetCurrentLocationAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
        sut.UserLocation.Should().BeNull();
        sut.StatusMessage.Should().Contain("геолокац");
    }
}
