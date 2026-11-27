using FluentAssertions;
using Moq;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;
using PizzaTake.Core.Tests.Helpers;
using PizzaTake.Core.ViewModels;
using Xunit;

namespace PizzaTake.Core.Tests.Unit;

public sealed class AdditionalViewModelsTests
{
    [Fact]
    public async Task WelcomeViewModel_StartCommand_ShouldSaveDeliveryTypeAndNavigateToCities()
    {
        var appState = new Mock<IAppStateService>();
        var navigation = new Mock<INavigationService>();
        var sut = new WelcomeViewModel(appState.Object, navigation.Object);

        await sut.StartCommand.ExecuteAsync(null);

        appState.VerifySet(s => s.SelectedDeliveryType = DeliveryType.Delivery, Times.Once);
        appState.Verify(s => s.SaveState(), Times.Once);
        navigation.Verify(n => n.GoToAsync("cities", null), Times.Once);
    }

    [Fact]
    public async Task CitySelectionViewModel_LoadSearchSelectAndFindNearby_ShouldHandleGpsAndNearestPizzeria()
    {
        var kyiv = new City { Id = 1, Name = "Київ", Region = "Київська", IsPopular = true, Latitude = 50.4501, Longitude = 30.5234 };
        var lviv = new City { Id = 2, Name = "Львів", Region = "Львівська", IsPopular = true, Latitude = 49.8397, Longitude = 24.0297 };
        var kyivStore = new Pizzeria { Id = 10, CityId = 1, StoreNumber = "TT 78", Latitude = 50.4477, Longitude = 30.5228 };

        var cityRepo = new Mock<ICityRepository>();
        var pizzeriaRepo = new Mock<IPizzeriaRepository>();
        var profileRepo = new Mock<IUserProfileRepository>();
        var sync = new Mock<ISyncService>();
        var appState = new Mock<IAppStateService>();
        var nav = new Mock<INavigationService>();
        var location = new Mock<ILocationService>();
        var permissions = new Mock<IPermissionService>();
        var dispatcher = new ImmediateMainThreadDispatcher();

        cityRepo.Setup(r => r.GetPopularAsync()).ReturnsAsync([kyiv, lviv]);
        cityRepo.Setup(r => r.GetAllAsync()).ReturnsAsync([kyiv, lviv]);
        cityRepo.Setup(r => r.SearchAsync("Льв")).ReturnsAsync([lviv]);
        pizzeriaRepo.Setup(r => r.GetByCityIdAsync(1)).ReturnsAsync([kyivStore]);
        profileRepo.Setup(r => r.GetCurrentAsync()).ReturnsAsync(new UserProfile { Id = 1 });
        permissions
            .Setup(p => p.EnsureGrantedAsync(AppPermission.LocationWhenInUse, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        location
            .Setup(l => l.GetCurrentLocationAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationResult(new GeoPoint(50.4480, 30.5230)));

        var sut = new CitySelectionViewModel(
            cityRepo.Object,
            appState.Object,
            nav.Object,
            location.Object,
            dispatcher,
            pizzeriaRepo.Object,
            profileRepo.Object,
            sync.Object,
            permissions.Object);

        sut.ApplyQueryAttributes(new Dictionary<string, object> { ["returnTo"] = "settings" });
        await sut.LoadCommand.ExecuteAsync(null);
        sut.Cities.Should().HaveCount(2);

        sut.SearchText = "Льв";
        await sut.SearchAsync();
        sut.Cities.Should().ContainSingle(c => c.Name == "Львів");

        await sut.FindNearbyCommand.ExecuteAsync(null);

        appState.VerifySet(s => s.SelectedCity = kyiv, Times.Once);
        appState.VerifySet(s => s.SelectedPizzeria = kyivStore, Times.Once);
        sync.Verify(s => s.EnqueueAsync("UserProfile", "Update", It.IsAny<object>()), Times.Once);
        nav.Verify(n => n.GoToAsync("///settings", null), Times.Once);

        await sut.SelectCityCommand.ExecuteAsync(lviv);
        nav.Verify(n => n.GoToAsync("pizzerias?returnTo=settings", null), Times.Once);
    }

    [Fact]
    public async Task PizzeriaSelectionViewModel_LoadSelectAndConfirm_ShouldSaveStateAndRouteToReturnTarget()
    {
        var kyiv = new City { Id = 1, Name = "Київ" };
        var p1 = new Pizzeria { Id = 1, CityId = 1, StoreNumber = "TT 78", Address = "вул. Хрещатик, 22" };
        var p2 = new Pizzeria { Id = 2, CityId = 1, StoreNumber = "TT 84", Address = "просп. Перемоги, 45" };

        var repo = new Mock<IPizzeriaRepository>();
        var appState = new Mock<IAppStateService>();
        var nav = new Mock<INavigationService>();
        appState.Setup(s => s.SelectedCity).Returns(kyiv);
        appState.Setup(s => s.SelectedPizzeria).Returns(p1);
        repo.Setup(r => r.GetByCityIdAsync(1)).ReturnsAsync([p1, p2]);

        var sut = new PizzeriaSelectionViewModel(repo.Object, appState.Object, nav.Object);
        sut.ApplyQueryAttributes(new Dictionary<string, object> { ["returnTo"] = "cart" });
        await sut.LoadCommand.ExecuteAsync(null);

        sut.Pizzerias.Should().HaveCount(2);
        sut.SelectedPizzeria.Should().Be(p1);
        p1.IsSelected.Should().BeTrue();

        sut.SelectPizzeriaCommand.Execute(p2);
        p2.IsSelected.Should().BeTrue();
        p1.IsSelected.Should().BeFalse();

        await sut.ConfirmPizzeriaCommand.ExecuteAsync(null);
        appState.VerifySet(s => s.SelectedPizzeria = p2, Times.Once);
        appState.Verify(s => s.SaveState(), Times.Once);
        nav.Verify(n => n.GoToAsync("///cart", null), Times.Once);
    }

    [Fact]
    public async Task PizzaDetailViewModel_ToggleOptionsFavoriteAndAddToCart_ShouldRecalculatePriceAndPersist()
    {
        var pizza = new Product
        {
            Id = 3,
            Name = "Маргарита",
            Category = "Standard",
            BasePrice = 259m,
            Calories = 220,
            Proteins = 10m,
            Fats = 8m,
            Carbs = 28m
        };
        var extraCheese = new IngredientOption { Id = 11, ProductId = 3, Name = "Додаткова моцарела", Price = 35m, Kind = OptionKind.AddOn };
        var noTomato = new IngredientOption { Id = 12, ProductId = 3, Name = "Без томатів", Price = 0m, Kind = OptionKind.Exclusion };
        var fetaSwap = new IngredientOption { Id = 13, ProductId = 3, Name = "Заміна на фету", Price = 15m, Kind = OptionKind.Replacement };

        var productRepo = new Mock<IProductRepository>();
        var cartRepo = new Mock<ICartRepository>();
        var favRepo = new Mock<IFavoriteRepository>();
        var nav = new Mock<INavigationService>();
        var dialog = new Mock<IDialogService>();
        var crash = new Mock<ICrashReportingService>();

        productRepo.Setup(r => r.GetOptionsByProductIdAsync(3)).ReturnsAsync([extraCheese, noTomato, fetaSwap]);
        favRepo.Setup(r => r.GetFavoriteProductsAsync()).ReturnsAsync([]);

        var sut = new PizzaDetailViewModel(productRepo.Object, cartRepo.Object, favRepo.Object, nav.Object, dialog.Object, crashReporting: crash.Object);
        sut.ApplyQueryAttributes(new Dictionary<string, object> { ["Product"] = pizza });
        await sut.LoadCommand.ExecuteAsync(null);

        sut.KbjuSummary.Should().Contain("220");
        sut.AddOns.Should().ContainSingle();
        sut.Exclusions.Should().ContainSingle();
        sut.Replacements.Should().ContainSingle();

        sut.ToggleKbjuCommand.Execute(null);
        sut.IsKbjuVisible.Should().BeTrue();

        sut.ToggleOptionCommand.Execute(extraCheese);
        sut.ToggleOptionCommand.Execute(fetaSwap);
        sut.CalculatedPrice.Should().Be(309m);
        sut.SelectedOptionsSummary.Should().Contain("Додаткова моцарела").And.Contain("Заміна на фету");

        await sut.ToggleFavoriteCommand.ExecuteAsync(null);
        sut.IsFavorite.Should().BeTrue();
        sut.FavoriteButtonText.Should().Be("♥");

        await sut.AddToCartCommand.ExecuteAsync(null);
        cartRepo.Verify(r => r.SaveAsync(It.Is<CartItem>(c =>
            c.ProductId == 3 && c.UnitPrice == 309m && c.Quantity == 1)), Times.Once);
        nav.Verify(n => n.GoBackAsync(null), Times.Once);
        crash.Verify(c => c.TrackEvent(CrashEventNames.ProductOpened, It.IsAny<IDictionary<string, string>>()), Times.Once);
    }

    [Fact]
    public async Task FavoritesViewModel_LoadAddToCartAndToggleFavorite_ShouldUpdateStateAndNavigate()
    {
        var pizza = new Product { Id = 1, Name = "Чеддерман", BasePrice = 219m };
        var favRepo = new Mock<IFavoriteRepository>();
        var cartRepo = new Mock<ICartRepository>();
        var nav = new Mock<INavigationService>();

        favRepo.Setup(r => r.GetFavoriteProductsAsync()).ReturnsAsync([pizza]);

        var sut = new FavoritesViewModel(favRepo.Object, cartRepo.Object, nav.Object);
        await sut.LoadCommand.ExecuteAsync(null);
        sut.Favorites.Should().ContainSingle(p => p.Id == 1);

        await sut.AddToCartCommand.ExecuteAsync(pizza);
        cartRepo.Verify(c => c.SaveAsync(It.Is<CartItem>(i => i.ProductId == 1 && i.UnitPrice == 219m)), Times.Once);
        sut.HasStatusMessage.Should().BeTrue();

        await sut.OpenProductCommand.ExecuteAsync(pizza);
        nav.Verify(n => n.GoToAsync("pizzadetail", It.IsAny<IDictionary<string, object>>()), Times.Once);

        await sut.ToggleFavoriteCommand.ExecuteAsync(pizza);
        favRepo.Verify(f => f.ToggleFavoriteAsync(1), Times.Once);
    }

    [Fact]
    public async Task OrderHistoryViewModel_WhenLockedVsUnlocked_ShouldEnforcePrivacyAndOpenDetails()
    {
        var order = new Order { Id = 5, OrderNumber = "PD-5005", TotalAmount = 259m };
        var orderRepo = new Mock<IOrderRepository>();
        var auth = new Mock<IAuthService>();
        var session = new Mock<ISessionService>();
        var nav = new Mock<INavigationService>();
        var dispatcher = new ImmediateMainThreadDispatcher();

        orderRepo.Setup(r => r.GetHistoryAsync()).ReturnsAsync([order]);
        auth.Setup(a => a.HasSavedCredentialsAsync()).ReturnsAsync(true);
        session.Setup(s => s.IsAuthenticated).Returns(false);

        var sut = new OrderHistoryViewModel(orderRepo.Object, auth.Object, session.Object, nav.Object, dispatcher);
        await sut.LoadHistoryCommand.ExecuteAsync(null);

        sut.IsSessionLocked.Should().BeTrue();
        sut.PastOrders.Should().BeEmpty();
        sut.IsEmpty.Should().BeFalse();

        await sut.OpenDetailCommand.ExecuteAsync(order);
        nav.Verify(n => n.GoToAsync("auth?locked=true", null), Times.Once);

        session.Setup(s => s.IsAuthenticated).Returns(true);
        await sut.LoadHistoryCommand.ExecuteAsync(null);

        sut.IsSessionLocked.Should().BeFalse();
        sut.PastOrders.Should().ContainSingle(o => o.Id == 5);

        await sut.OpenDetailCommand.ExecuteAsync(order);
        nav.Verify(n => n.GoToAsync("orderdetail", It.IsAny<IDictionary<string, object>>()), Times.Once);
    }

    [Fact]
    public async Task AdminProductEditViewModel_ValidationSaveUpdateAndDelete_ShouldEnforceAdminRoleAndPromoRules()
    {
        var productRepo = new Mock<IProductRepository>();
        var session = new Mock<ISessionService>();
        var sync = new Mock<ISyncService>();
        var nav = new Mock<INavigationService>();
        var dialog = new Mock<IDialogService>();
        var dispatcher = new ImmediateMainThreadDispatcher();

        session.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(false);
        session.Setup(s => s.CurrentRole).Returns(UserRole.Customer);

        var sut = new AdminProductEditViewModel(
            productRepo.Object,
            session.Object,
            sync.Object,
            nav.Object,
            dialog.Object,
            dispatcher);

        sut.Name = "Чотири сири";
        sut.BasePriceInput = "310";
        await sut.SaveProductCommand.ExecuteAsync(null);
        dialog.Verify(d => d.AlertAsync("Доступ заборонено", It.IsAny<string>(), "OK"), Times.Once);
        productRepo.Verify(r => r.SaveAsync(It.IsAny<Product>()), Times.Never);

        session.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(true);
        session.Setup(s => s.CurrentRole).Returns(UserRole.Administrator);
        sut.StartSessionMonitoring();
        sut.IsAdmin.Should().BeTrue();

        sut.PromoPriceInput = "350";
        await sut.SaveProductCommand.ExecuteAsync(null);
        sut.HasError.Should().BeTrue();
        sut.ErrorMessage.Should().Contain("меншою за базову");

        sut.PromoPriceInput = "279";
        sut.WeightGramsInput = "550";
        sut.CaloriesInput = "260";
        await sut.SaveProductCommand.ExecuteAsync(null);
        productRepo.Verify(r => r.SaveAsync(It.Is<Product>(p => p.Name == "Чотири сири" && p.PromoPrice == 279m)), Times.Once);
        sync.Verify(s => s.EnqueueAsync("Product", "Insert", It.IsAny<object>()), Times.Once);

        var existing = new Product { Id = 4, Name = "Діабло", BasePrice = 289m, Category = "WOW піци", WeightGrams = 540, Calories = 250 };
        sut.ApplyQueryAttributes(new Dictionary<string, object> { ["Product"] = existing });
        sut.CanDeleteProduct.Should().BeTrue();
        dialog.Setup(d => d.ConfirmAsync("Видалення піци", It.IsAny<string>(), "Видалити", "Скасувати")).ReturnsAsync(true);

        await sut.DeleteProductCommand.ExecuteAsync(null);
        productRepo.Verify(r => r.DeleteAsync(existing), Times.Once);
        sync.Verify(s => s.EnqueueAsync("Product", "Delete", It.IsAny<object>()), Times.Once);
        sut.StopSessionMonitoring();
    }

    [Theory]
    [InlineData(259, 207, true, -20, "-20%")]
    [InlineData(200, 150, true, -25, "-25%")]
    [InlineData(259, null, false, 0, "")]
    [InlineData(259, 259, false, 0, "")]
    public void Product_PromoDiscountFormula_ShouldComputePercentageAndBadge(
        decimal basePrice,
        double? promoPrice,
        bool expectedHasPromo,
        int expectedPercent,
        string expectedBadge)
    {
        var product = new Product
        {
            BasePrice = basePrice,
            PromoPrice = promoPrice.HasValue ? (decimal)promoPrice.Value : null
        };

        product.HasPromo.Should().Be(expectedHasPromo);
        product.DiscountPercent.Should().Be(expectedPercent);
        product.DiscountBadgeText.Should().Be(expectedBadge);
    }

    [Fact]
    public async Task PizzeriaSelectionViewModel_EmbeddedMapLocationAndExternalLauncher_ShouldFocusAndSelectNearest()
    {
        var kyiv = new City { Id = 1, Name = "Київ" };
        var p1 = new Pizzeria { Id = 1, CityId = 1, StoreNumber = "TT 78", Address = "вул. Хрещатик, 22", Latitude = 50.4477, Longitude = 30.5228 };
        var p2 = new Pizzeria { Id = 2, CityId = 1, StoreNumber = "TT 84", Address = "просп. Перемоги, 45", Latitude = 50.4500, Longitude = 30.4500 };

        var repo = new Mock<IPizzeriaRepository>();
        var appState = new Mock<IAppStateService>();
        var nav = new Mock<INavigationService>();
        var mapLauncher = new Mock<IMapLauncherService>();
        var location = new Mock<ILocationService>();
        var permission = new Mock<IPermissionService>();

        appState.Setup(s => s.SelectedCity).Returns(kyiv);
        repo.Setup(r => r.GetByCityIdAsync(1)).ReturnsAsync([p1, p2]);
        permission.Setup(p => p.EnsureGrantedAsync(AppPermission.LocationWhenInUse, It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        location.Setup(l => l.GetCurrentLocationAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationResult(new GeoPoint(50.4501, 30.4510)));
        mapLauncher.Setup(m => m.OpenExternalMapAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<string>())).ReturnsAsync(true);

        var sut = new PizzeriaSelectionViewModel(
            repo.Object,
            appState.Object,
            nav.Object,
            mapLauncherService: mapLauncher.Object,
            locationService: location.Object,
            permissionService: permission.Object);

        GeoPoint? focused = null;
        sut.FocusRequested += (_, pt) => focused = pt;

        await sut.LoadCommand.ExecuteAsync(null);
        await sut.CenterOnMyLocationCommand.ExecuteAsync(null);

        sut.SelectedPizzeria.Should().Be(p2);
        sut.UserLocation.Should().Be(new GeoPoint(50.4501, 30.4510));
        focused.Should().Be(new GeoPoint(50.4501, 30.4510));

        await sut.OpenExternalMapCommand.ExecuteAsync(null);
        mapLauncher.Verify(m => m.OpenExternalMapAsync(50.4500, 30.4500, It.Is<string>(s => s.Contains("TT 84"))), Times.Once);
    }
}
