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
}
