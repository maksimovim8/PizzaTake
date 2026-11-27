using FluentAssertions;
using Moq;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;
using PizzaTake.Core.Tests.Helpers;
using PizzaTake.Core.ViewModels;
using Xunit;

namespace PizzaTake.Core.Tests.Unit;

public sealed class CartAndSettingsViewModelTests
{
    [Fact]
    public async Task CartViewModel_LoadAndProceedCheckout_ShouldCalculateTotalsAndRouteToAuthWhenGuest()
    {
        var cartRepo = new Mock<ICartRepository>();
        var productRepo = new Mock<IProductRepository>();
        var auth = new Mock<IAuthService>();
        var appState = new Mock<IAppStateService>();
        var nav = new Mock<INavigationService>();
        var dialog = new Mock<IDialogService>();

        cartRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(
        [
            new CartItem { Id = 1, ProductId = 1, ProductName = "Чеддерман", Quantity = 2, UnitPrice = 219m }
        ]);
        productRepo.Setup(r => r.GetAllAsync()).ReturnsAsync([]);
        appState.Setup(s => s.SelectedPizzeria).Returns(new Pizzeria { Id = 1, StoreNumber = "TT 78" });
        auth.Setup(a => a.IsAuthenticated).Returns(false);

        var sut = new CartViewModel(cartRepo.Object, productRepo.Object, auth.Object, appState.Object, nav.Object, dialog.Object);
        await sut.LoadCartCommand.ExecuteAsync(null);

        sut.Subtotal.Should().Be(438m);
        sut.Total.Should().Be(438m);
        sut.HasItems.Should().BeTrue();

        await sut.ProceedCheckoutCommand.ExecuteAsync(null);
        nav.Verify(n => n.GoToAsync("auth?next=checkout", null), Times.Once);
    }

    [Fact]
    public async Task SettingsViewModel_OpenLogsCommand_ShouldEnforceAdminRole()
    {
        var profileRepo = new Mock<IUserProfileRepository>();
        var addressRepo = new Mock<IAddressRepository>();
        var auth = new Mock<IAuthService>();
        var session = new Mock<ISessionService>();
        var appState = new Mock<IAppStateService>();
        var reset = new Mock<IUserDataResetService>();
        var sync = new Mock<ISyncService>();
        var nav = new Mock<INavigationService>();
        var dialog = new Mock<IDialogService>();
        var dispatcher = new ImmediateMainThreadDispatcher();

        session.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(false);
        session.Setup(s => s.CurrentRole).Returns(UserRole.Customer);

        var sut = new SettingsViewModel(
            profileRepo.Object,
            addressRepo.Object,
            auth.Object,
            session.Object,
            appState.Object,
            reset.Object,
            sync.Object,
            nav.Object,
            dialog.Object,
            dispatcher);

        await sut.OpenLogsCommand.ExecuteAsync(null);

        dialog.Verify(d => d.AlertAsync("Доступ заборонено", It.IsAny<string>(), "OK"), Times.Once);
        nav.Verify(n => n.GoToAsync("logs", It.IsAny<IDictionary<string, object>?>()), Times.Never);
    }

    [Fact]
    public async Task CartViewModel_QtyUpsellClearAndOfflineMonitoring_ShouldUpdateTotalsAndBanner()
    {
        var cartRepo = new Mock<ICartRepository>();
        var productRepo = new Mock<IProductRepository>();
        var auth = new Mock<IAuthService>();
        var appState = new Mock<IAppStateService>();
        var nav = new Mock<INavigationService>();
        var dialog = new Mock<IDialogService>();
        var dispatcher = new ImmediateMainThreadDispatcher();
        var connectivity = new Mock<IConnectivityService>();
        var sync = new Mock<ISyncService>();

        var item = new CartItem { Id = 1, ProductId = 1, ProductName = "Маргарита", Quantity = 1, UnitPrice = 259m };
        cartRepo.Setup(r => r.GetAllAsync()).ReturnsAsync([item]);
        productRepo.Setup(r => r.GetAllAsync()).ReturnsAsync([]);
        connectivity.Setup(c => c.IsConnected).Returns(false);
        sync.Setup(s => s.GetPendingCountAsync()).ReturnsAsync(2);

        var sut = new CartViewModel(
            cartRepo.Object,
            productRepo.Object,
            auth.Object,
            appState.Object,
            nav.Object,
            dialog.Object,
            dispatcher,
            connectivity.Object,
            sync.Object);

        sut.StartConnectivityMonitoring();
        sut.IsOffline.Should().BeTrue();
        sut.OfflineBannerText.Should().Contain("2");

        await sut.LoadCartCommand.ExecuteAsync(null);
        sut.UpsellItems.Should().NotBeEmpty();

        await sut.UpdateQtyCommand.ExecuteAsync(sut.Items[0]);
        sut.Total.Should().Be(518m);

        await sut.AddUpsellCommand.ExecuteAsync(sut.UpsellItems[0]);
        sut.Items.Should().HaveCount(2);

        await sut.ClearCartCommand.ExecuteAsync(null);
        sut.IsEmpty.Should().BeTrue();
        sut.Total.Should().Be(0m);
        sut.StopConnectivityMonitoring();
    }

    [Fact]
    public async Task SettingsViewModel_LoadThemeSyncLockAndLab7Routes_ShouldWorkCorrectly()
    {
        var profileRepo = new Mock<IUserProfileRepository>();
        var addressRepo = new Mock<IAddressRepository>();
        var auth = new Mock<IAuthService>();
        var session = new Mock<ISessionService>();
        var appState = new Mock<IAppStateService>();
        var reset = new Mock<IUserDataResetService>();
        var sync = new Mock<ISyncService>();
        var nav = new Mock<INavigationService>();
        var dialog = new Mock<IDialogService>();
        var dispatcher = new ImmediateMainThreadDispatcher();

        auth.Setup(a => a.HasSavedCredentialsAsync()).ReturnsAsync(true);
        auth.Setup(a => a.IsAuthenticated).Returns(true);
        session.Setup(s => s.CurrentRole).Returns(UserRole.Customer);
        profileRepo.Setup(r => r.GetCurrentAsync()).ReturnsAsync(new UserProfile
        {
            Id = 1,
            CityName = "Київ",
            Phone = "+380671234567",
            IsPhoneVerified = true
        });
        addressRepo.Setup(r => r.GetAllAsync()).ReturnsAsync([new UserAddress { Id = 1, Label = "Дім", Text = "вул. Хрещатик, 22" }]);
        sync.Setup(s => s.GetPendingCountAsync()).ReturnsAsync(1);

        var sut = new SettingsViewModel(
            profileRepo.Object,
            addressRepo.Object,
            auth.Object,
            session.Object,
            appState.Object,
            reset.Object,
            sync.Object,
            nav.Object,
            dialog.Object,
            dispatcher);

        await sut.LoadSettingsCommand.ExecuteAsync(null);

        sut.CurrentCityName.Should().Be("Київ");
        sut.MaskedPhone.Should().Be("+380 *** **67");
        sut.HasAddresses.Should().BeTrue();
        sut.SyncStatusText.Should().Contain("1");

        sut.ToggleThemeCommand.Execute("Dark");
        appState.Verify(s => s.ApplyTheme(true), Times.Once);

        await sut.OpenMapCommand.ExecuteAsync(null);
        await sut.OpenQualityReportCommand.ExecuteAsync(null);
        nav.Verify(n => n.GoToAsync("pizzeriasmap", null), Times.Once);
        nav.Verify(n => n.GoToAsync("qualityreport", null), Times.Once);

        await sut.LockSessionCommand.ExecuteAsync(null);
        auth.Verify(a => a.LockSessionAsync(), Times.Once);
        sut.IsSessionLocked.Should().BeTrue();
        nav.Verify(n => n.GoToAsync("auth?locked=true", null), Times.Once);

        await sut.LogoutCommand.ExecuteAsync(null);
        reset.Verify(r => r.ResetForLogoutAsync(), Times.Once);
        nav.Verify(n => n.GoToAsync("///welcome-start", null), Times.Once);
    }

    [Fact]
    public async Task SettingsViewModel_WhenGuestHasNoProfile_ShouldStillLoadSavedAddressesAndApplyQueryUpdates()
    {
        var profileRepo = new Mock<IUserProfileRepository>();
        var addressRepo = new Mock<IAddressRepository>();
        var auth = new Mock<IAuthService>();
        var session = new Mock<ISessionService>();
        var appState = new Mock<IAppStateService>();
        var reset = new Mock<IUserDataResetService>();
        var sync = new Mock<ISyncService>();
        var nav = new Mock<INavigationService>();
        var dialog = new Mock<IDialogService>();
        var dispatcher = new ImmediateMainThreadDispatcher();

        auth.Setup(a => a.HasSavedCredentialsAsync()).ReturnsAsync(false);
        auth.Setup(a => a.IsAuthenticated).Returns(false);
        session.Setup(s => s.CurrentRole).Returns(UserRole.Customer);
        appState.Setup(s => s.SelectedCity).Returns(new City { Id = 1, Name = "Київ" });
        profileRepo.Setup(r => r.GetCurrentAsync()).ReturnsAsync((UserProfile?)null);
        addressRepo.Setup(r => r.GetAllAsync()).ReturnsAsync([new UserAddress { Id = 10, Label = "Офіс", Text = "вул. Богдана Хмельницького, 5" }]);

        var sut = new SettingsViewModel(
            profileRepo.Object,
            addressRepo.Object,
            auth.Object,
            session.Object,
            appState.Object,
            reset.Object,
            sync.Object,
            nav.Object,
            dialog.Object,
            dispatcher);

        await sut.LoadSettingsCommand.ExecuteAsync(null);

        sut.CurrentCityName.Should().Be("Київ");
        sut.HasAddresses.Should().BeTrue();
        sut.Addresses.Should().ContainSingle(a => a.Id == 10 && a.Label == "Офіс");

        sut.ApplyQueryAttributes(new Dictionary<string, object>
        {
            ["UpdatedAddress"] = new UserAddress { Id = 10, Label = "Офіс 2", Text = "вул. Саксаганського, 12" }
        });
        sut.Addresses.Should().ContainSingle(a => a.Id == 10 && a.Label == "Офіс 2");

        sut.ApplyQueryAttributes(new Dictionary<string, object>
        {
            ["DeletedAddressId"] = "10"
        });
        sut.Addresses.Should().BeEmpty();
    }

    [Fact]
    public async Task SettingsViewModel_WhenSessionLocked_ShouldNotNavigateOnEditAddress()
    {
        var profileRepo = new Mock<IUserProfileRepository>();
        var addressRepo = new Mock<IAddressRepository>();
        var auth = new Mock<IAuthService>();
        var session = new Mock<ISessionService>();
        var appState = new Mock<IAppStateService>();
        var reset = new Mock<IUserDataResetService>();
        var sync = new Mock<ISyncService>();
        var nav = new Mock<INavigationService>();
        var dialog = new Mock<IDialogService>();
        var dispatcher = new ImmediateMainThreadDispatcher();

        auth.Setup(a => a.HasSavedCredentialsAsync()).ReturnsAsync(true);
        auth.Setup(a => a.IsAuthenticated).Returns(false);
        session.Setup(s => s.CurrentRole).Returns(UserRole.Customer);
        profileRepo.Setup(r => r.GetCurrentAsync()).ReturnsAsync(new UserProfile { Id = 1, CityName = "Київ" });

        var sut = new SettingsViewModel(
            profileRepo.Object,
            addressRepo.Object,
            auth.Object,
            session.Object,
            appState.Object,
            reset.Object,
            sync.Object,
            nav.Object,
            dialog.Object,
            dispatcher);

        await sut.LoadSettingsCommand.ExecuteAsync(null);
        await sut.EditAddressCommand.ExecuteAsync(new UserAddress { Id = 1, Label = "Дім", Text = "вул. Хрещатик, 1" });

        sut.IsSessionLocked.Should().BeTrue();
        nav.Verify(n => n.GoToAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>?>()), Times.Never);
    }
}
