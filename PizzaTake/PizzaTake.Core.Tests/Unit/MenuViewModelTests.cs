using FluentAssertions;
using Moq;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;
using PizzaTake.Core.Tests.Helpers;
using PizzaTake.Core.ViewModels;
using Xunit;

namespace PizzaTake.Core.Tests.Unit;

public sealed class MenuViewModelTests
{
    private readonly Mock<IProductRepository> productRepo = new();
    private readonly Mock<ICartRepository> cartRepo = new();
    private readonly Mock<IOrderRepository> orderRepo = new();
    private readonly Mock<IAppStateService> appState = new();
    private readonly Mock<IAuthService> authService = new();
    private readonly Mock<ISessionService> sessionService = new();
    private readonly Mock<INavigationService> navigation = new();
    private readonly Mock<IDialogService> dialogService = new();
    private readonly ImmediateMainThreadDispatcher dispatcher = new();

    private MenuViewModel CreateSut() => new(
        productRepo.Object,
        cartRepo.Object,
        orderRepo.Object,
        appState.Object,
        authService.Object,
        sessionService.Object,
        navigation.Object,
        dialogService.Object,
        dispatcher);

    [Fact]
    public async Task LoadAndFilter_ShouldLoadCatalogAndFilterByCategoryAndSearchText()
    {
        var catalog = new List<Product>
        {
            new() { Id = 1, Name = "Чеддерман", Category = "Новинки", BasePrice = 219m },
            new() { Id = 2, Name = "Діжон", Category = "WOW піци", BasePrice = 299m },
            new() { Id = 3, Name = "Маргарита", Category = "Standard", BasePrice = 259m }
        };
        productRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(catalog);
        authService.Setup(a => a.HasSavedCredentialsAsync()).ReturnsAsync(false);
        orderRepo.Setup(r => r.GetActiveAsync()).ReturnsAsync((Order?)null);

        var sut = CreateSut();
        await sut.LoadCommand.ExecuteAsync(null);

        sut.Products.Should().HaveCount(3);
        sut.Categories.Should().Contain(["Усі категорії", "Новинки", "WOW піци", "Standard"]);

        sut.SelectedCategory = "WOW піци";
        await sut.ApplyFilterAsync();
        sut.Products.Should().ContainSingle(p => p.Name == "Діжон");

        sut.SelectedCategory = "Усі категорії";
        sut.SearchText = "марг";
        await sut.ApplyFilterAsync();
        sut.Products.Should().ContainSingle(p => p.Name == "Маргарита");
    }

    [Fact]
    public async Task OpenProductCommand_ShouldNavigateToPizzaDetailWithProductParameter()
    {
        var pizza = new Product { Id = 7, Name = "Пепероні", Category = "Standard", BasePrice = 279m };
        var sut = CreateSut();

        await sut.OpenProductCommand.ExecuteAsync(pizza);

        navigation.Verify(n => n.GoToAsync(
            "pizzadetail",
            It.Is<IDictionary<string, object>>(d =>
                d.ContainsKey("Product") && ReferenceEquals(d["Product"], pizza))),
            Times.Once);
    }

    [Fact]
    public async Task OpenAdminProductCommand_WhenCustomer_ShouldDenyAccessAndShowAlert()
    {
        sessionService.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(false);
        sessionService.Setup(s => s.CurrentRole).Returns(UserRole.Customer);
        var sut = CreateSut();

        await sut.OpenAdminProductCommand.ExecuteAsync(null);

        dialogService.Verify(d => d.AlertAsync("Доступ заборонено", It.IsAny<string>(), "OK"), Times.Once);
        navigation.Verify(n => n.GoToAsync("adminproductedit", It.IsAny<IDictionary<string, object>?>()), Times.Never);
    }

    [Fact]
    public async Task ConnectivityMonitoringAndLab7NavigationCommands_ShouldUpdateOfflineBannerAndRoute()
    {
        var connectivity = new Mock<IConnectivityService>();
        var sync = new Mock<ISyncService>();
        connectivity.Setup(c => c.IsConnected).Returns(false);
        sync.Setup(s => s.GetPendingCountAsync()).ReturnsAsync(3);

        var sut = new MenuViewModel(
            productRepo.Object,
            cartRepo.Object,
            orderRepo.Object,
            appState.Object,
            authService.Object,
            sessionService.Object,
            navigation.Object,
            dialogService.Object,
            dispatcher,
            connectivity.Object,
            sync.Object);

        sut.StartConnectivityMonitoring();
        sut.IsOffline.Should().BeTrue();
        sut.OfflineBannerText.Should().Contain("3");

        connectivity.Raise(c => c.ConnectivityChanged += null, connectivity.Object, true);
        sut.IsOffline.Should().BeFalse();

        await sut.OpenMapCommand.ExecuteAsync(null);
        await sut.OpenQualityReportCommand.ExecuteAsync(null);
        navigation.Verify(n => n.GoToAsync("pizzeriasmap", null), Times.Once);
        navigation.Verify(n => n.GoToAsync("qualityreport", null), Times.Once);

        var pizza = new Product { Id = 1, Name = "Чеддерман", BasePrice = 219m };
        await sut.QuickAddCommand.ExecuteAsync(pizza);
        cartRepo.Verify(c => c.SaveAsync(It.Is<CartItem>(i => i.ProductId == 1)), Times.Once);
        sut.HasStatusMessage.Should().BeTrue();

        sut.StopConnectivityMonitoring();
    }
}
