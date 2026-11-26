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
}
