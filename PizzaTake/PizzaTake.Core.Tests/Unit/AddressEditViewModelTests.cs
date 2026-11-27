using FluentAssertions;
using Moq;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;
using PizzaTake.Core.ViewModels;
using Xunit;

namespace PizzaTake.Core.Tests.Unit;

public sealed class AddressEditViewModelTests
{
    private readonly Mock<IAddressRepository> addressRepo = new();
    private readonly Mock<IUserProfileRepository> profileRepo = new();
    private readonly Mock<IAppStateService> appState = new();
    private readonly Mock<ISyncService> syncService = new();
    private readonly Mock<INavigationService> navigation = new();

    private AddressEditViewModel CreateSut() => new(
        addressRepo.Object,
        profileRepo.Object,
        appState.Object,
        syncService.Object,
        navigation.Object);

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("вул. Хрещатик, 22", true)]
    public void SaveCommand_CanExecute_ShouldReflectAddressTextValidity(string addressText, bool expectedCanExecute)
    {
        var sut = CreateSut();
        sut.AddressText = addressText;

        sut.SaveCommand.CanExecute(null).Should().Be(expectedCanExecute);
    }

    [Fact]
    public async Task SaveCommand_WithValidAddress_ShouldSaveEnqueueSyncAndNavigateBackWithUpdatedAddress()
    {
        profileRepo.Setup(r => r.GetCurrentAsync()).ReturnsAsync(new UserProfile { Id = 1 });
        addressRepo
            .Setup(r => r.SaveAsync(It.IsAny<UserAddress>()))
            .Callback<UserAddress>(a => a.Id = 42)
            .ReturnsAsync(1);

        var sut = CreateSut();
        sut.Label = "Офіс";
        sut.AddressText = "вул. Хрещатик, 22, кв. 10";
        sut.Notes = "Код 123";

        await sut.SaveCommand.ExecuteAsync(null);

        addressRepo.Verify(r => r.SaveAsync(It.Is<UserAddress>(a =>
            a.Label == "Офіс" &&
            a.Text == "вул. Хрещатик, 22, кв. 10" &&
            a.Notes == "Код 123")), Times.Once);

        syncService.Verify(s => s.EnqueueAsync("UserAddress", "Insert", It.IsAny<object>()), Times.Once);

        navigation.Verify(n => n.GoBackAsync(It.Is<IDictionary<string, object>>(p =>
            p.ContainsKey("UpdatedAddress") &&
            p["UpdatedAddress"] is UserAddress &&
            ((UserAddress)p["UpdatedAddress"]).Id == 42)), Times.Once);
    }

    [Fact]
    public async Task DeleteCommand_InEditMode_ShouldDeleteEnqueueSyncAndReturnDeletedId()
    {
        var existing = new UserAddress { Id = 15, Label = "Дім", Text = "вул. Саксаганського, 10" };
        var sut = CreateSut();
        sut.ApplyQueryAttributes(new Dictionary<string, object> { ["Address"] = existing });

        await sut.DeleteCommand.ExecuteAsync(null);

        addressRepo.Verify(r => r.DeleteAsync(existing), Times.Once);
        syncService.Verify(s => s.EnqueueAsync("UserAddress", "Delete", It.IsAny<object>()), Times.Once);
        navigation.Verify(n => n.GoBackAsync(It.Is<IDictionary<string, object>>(p =>
            p.ContainsKey("DeletedAddressId") && (int)p["DeletedAddressId"] == 15)), Times.Once);
    }

    [Fact]
    public async Task GetMyCoordinatesCommand_WhenGrantedAndGpsSucceeds_ShouldPopulateCoordinates()
    {
        var locationService = new Mock<ILocationService>();
        var permissionService = new Mock<IPermissionService>();
        permissionService
            .Setup(p => p.EnsureGrantedAsync(AppPermission.LocationWhenInUse, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        locationService
            .Setup(l => l.GetCurrentLocationAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationResult(new GeoPoint(50.4477, 30.5228)));

        var sut = new AddressEditViewModel(
            addressRepo.Object,
            profileRepo.Object,
            appState.Object,
            syncService.Object,
            navigation.Object,
            locationService.Object,
            permissionService.Object);

        await sut.GetMyCoordinatesCommand.ExecuteAsync(null);

        sut.HasCoordinates.Should().BeTrue();
        sut.Latitude.Should().BeApproximately(50.4477, 0.0001);
        sut.Longitude.Should().BeApproximately(30.5228, 0.0001);
        sut.CoordinatesText.Should().Contain("50.44770");
        sut.HasStatusMessage.Should().BeTrue();
    }

    [Fact]
    public async Task SaveCommand_WithTooShortAddress_ShouldSetValidationErrorAndNotPersist()
    {
        var sut = CreateSut();
        sut.AddressText = "вул";

        await sut.SaveCommand.ExecuteAsync(null);

        sut.HasError.Should().BeTrue();
        addressRepo.Verify(r => r.SaveAsync(It.IsAny<UserAddress>()), Times.Never);
    }
}
