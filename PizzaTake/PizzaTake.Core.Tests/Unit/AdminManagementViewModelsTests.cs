using FluentAssertions;
using Moq;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;
using PizzaTake.Core.Tests.Helpers;
using PizzaTake.Core.ViewModels;
using Xunit;

namespace PizzaTake.Core.Tests.Unit;

public sealed class AdminManagementViewModelsTests
{
    [Fact]
    public async Task AdminProductEditViewModel_SaveKbjuAndIngredientOptions_ShouldPersistAndEnforceAdminRbac()
    {
        var productRepo = new Mock<IProductRepository>();
        var categoryRepo = new Mock<ICategoryRepository>();
        var session = new Mock<ISessionService>();
        var sync = new Mock<ISyncService>();
        var nav = new Mock<INavigationService>();
        var dialog = new Mock<IDialogService>();
        var dispatcher = new ImmediateMainThreadDispatcher();

        categoryRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(
        [
            new ProductCategory { Id = 1, Name = "Новинки", SortOrder = 0 },
            new ProductCategory { Id = 2, Name = "Закуски", SortOrder = 1 }
        ]);
        productRepo.Setup(r => r.SaveAsync(It.IsAny<Product>()))
            .Callback<Product>(p => p.Id = 77)
            .Returns(Task.CompletedTask);

        session.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(true);
        session.Setup(s => s.CurrentRole).Returns(UserRole.Administrator);

        var sut = new AdminProductEditViewModel(
            productRepo.Object,
            session.Object,
            sync.Object,
            nav.Object,
            dialog.Object,
            dispatcher,
            categoryRepository: categoryRepo.Object);

        await sut.LoadCategoriesAsync();
        sut.AvailableCategories.Should().Contain(["Новинки", "Закуски"]);

        sut.Name = "Сирна Преміум";
        sut.Category = "Новинки";
        sut.BasePriceInput = "319";
        sut.WeightGramsInput = "580";
        sut.CaloriesInput = "285";
        sut.ProteinsInput = "15.5";
        sut.FatsInput = "12.2";
        sut.CarbsInput = "29.4";

        sut.NewOptionName = "Подвійний пармезан";
        sut.SelectedOptionKindName = "Додаток";
        sut.NewOptionPriceInput = "45";
        await sut.AddOptionCommand.ExecuteAsync(null);

        sut.NewOptionName = "Без цибулі";
        sut.SelectedOptionKindName = "Прибрати";
        await sut.AddOptionCommand.ExecuteAsync(null);

        sut.Options.Should().HaveCount(2);
        sut.HasOptions.Should().BeTrue();
        sut.Options[1].Price.Should().Be(0m);

        await sut.SaveProductCommand.ExecuteAsync(null);

        productRepo.Verify(r => r.SaveAsync(It.Is<Product>(p =>
            p.Calories == 285 &&
            p.Proteins == 15.5m &&
            p.Fats == 12.2m &&
            p.Carbs == 29.4m &&
            p.IsNew)), Times.Once);

        productRepo.Verify(r => r.SaveOptionAsync(It.Is<IngredientOption>(o =>
            o.ProductId == 77 && o.Name == "Подвійний пармезан" && o.Price == 45m && o.Kind == OptionKind.AddOn)), Times.Once);
        productRepo.Verify(r => r.SaveOptionAsync(It.Is<IngredientOption>(o =>
            o.ProductId == 77 && o.Name == "Без цибулі" && o.Price == 0m && o.Kind == OptionKind.Exclusion)), Times.Once);
    }

    [Fact]
    public async Task AdminCategoriesViewModel_AddRenameReorderDeleteAndRbac_ShouldWorkCorrectly()
    {
        var categoryRepo = new Mock<ICategoryRepository>();
        var productRepo = new Mock<IProductRepository>();
        var session = new Mock<ISessionService>();
        var nav = new Mock<INavigationService>();
        var dialog = new Mock<IDialogService>();
        var dispatcher = new ImmediateMainThreadDispatcher();

        session.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(false);
        session.Setup(s => s.CurrentRole).Returns(UserRole.Customer);

        var sut = new AdminCategoriesViewModel(
            categoryRepo.Object,
            productRepo.Object,
            session.Object,
            nav.Object,
            dialog.Object,
            dispatcher);

        await sut.LoadCommand.ExecuteAsync(null);
        dialog.Verify(d => d.AlertAsync("Доступ заборонено", It.IsAny<string>(), "OK"), Times.Once);
        nav.Verify(n => n.GoBackAsync(null), Times.Once);

        var cat1 = new ProductCategory { Id = 1, Name = "Новинки", SortOrder = 0 };
        var cat2 = new ProductCategory { Id = 2, Name = "Десерти", SortOrder = 1 };
        session.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(true);
        categoryRepo.Setup(r => r.GetAllAsync()).ReturnsAsync([cat1, cat2]);
        productRepo.Setup(r => r.GetByCategoryAsync("Десерти")).ReturnsAsync([]);
        dialog.Setup(d => d.ConfirmAsync("Видалення категорії", It.IsAny<string>(), "Видалити", "Скасувати")).ReturnsAsync(true);

        await sut.LoadCommand.ExecuteAsync(null);
        sut.Categories.Should().HaveCount(2);

        sut.NewCategoryName = "Комбо";
        await sut.SaveCategoryCommand.ExecuteAsync(null);
        categoryRepo.Verify(r => r.SaveAsync(It.Is<ProductCategory>(c => c.Name == "Комбо")), Times.Once);

        sut.StartEditCommand.Execute(cat2);
        sut.IsEditing.Should().BeTrue();
        sut.NewCategoryName = "Солодке";
        await sut.SaveCategoryCommand.ExecuteAsync(null);
        categoryRepo.Verify(r => r.RenameAsync(cat2, "Солодке"), Times.Once);

        cat2.Name = "Десерти";
        await sut.DeleteCategoryCommand.ExecuteAsync(cat2);
        categoryRepo.Verify(r => r.DeleteAsync(cat2), Times.Once);
    }

    [Fact]
    public async Task AdminBannersViewModel_CreateEditToggleDeleteAndRbac_ShouldWorkCorrectly()
    {
        var bannerRepo = new Mock<IMenuBannerRepository>();
        var session = new Mock<ISessionService>();
        var nav = new Mock<INavigationService>();
        var dialog = new Mock<IDialogService>();
        var dispatcher = new ImmediateMainThreadDispatcher();

        session.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(false);
        session.Setup(s => s.CurrentRole).Returns(UserRole.Customer);

        var sut = new AdminBannersViewModel(
            bannerRepo.Object,
            session.Object,
            nav.Object,
            dialog.Object,
            dispatcher);

        await sut.LoadCommand.ExecuteAsync(null);
        dialog.Verify(d => d.AlertAsync("Доступ заборонено", It.IsAny<string>(), "OK"), Times.Once);

        var banner = new MenuBanner { Id = 1, Eyebrow = "АКЦІЯ", Title = "Чеддерман", Subtitle = "Смачно", BadgeText = "219 ₴", IsActive = true };
        session.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(true);
        bannerRepo.Setup(r => r.GetAllAsync()).ReturnsAsync([banner]);
        dialog.Setup(d => d.ConfirmAsync("Видалення банера", It.IsAny<string>(), "Видалити", "Скасувати")).ReturnsAsync(true);

        await sut.LoadCommand.ExecuteAsync(null);
        sut.Banners.Should().ContainSingle();

        sut.Eyebrow = "НОВИНКА";
        sut.BannerTitle = "2+1 на піци";
        sut.Subtitle = "Щовихідних";
        sut.BadgeText = "-30%";
        await sut.SaveBannerCommand.ExecuteAsync(null);
        bannerRepo.Verify(r => r.SaveAsync(It.Is<MenuBanner>(b => b.Title == "2+1 на піци" && b.IsActive)), Times.Once);

        await sut.ToggleActiveCommand.ExecuteAsync(banner);
        banner.IsActive.Should().BeFalse();

        await sut.DeleteBannerCommand.ExecuteAsync(banner);
        bannerRepo.Verify(r => r.DeleteAsync(banner), Times.Once);
    }

    [Fact]
    public async Task MenuViewModel_DynamicCategoriesBannersAndAdminRoutes_ShouldEnforceRbacAndLoadDynamicContent()
    {
        var productRepo = new Mock<IProductRepository>();
        var cartRepo = new Mock<ICartRepository>();
        var orderRepo = new Mock<IOrderRepository>();
        var appState = new Mock<IAppStateService>();
        var auth = new Mock<IAuthService>();
        var session = new Mock<ISessionService>();
        var nav = new Mock<INavigationService>();
        var dialog = new Mock<IDialogService>();
        var dispatcher = new ImmediateMainThreadDispatcher();
        var categoryRepo = new Mock<ICategoryRepository>();
        var bannerRepo = new Mock<IMenuBannerRepository>();

        productRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(
        [
            new Product { Id = 1, Name = "Картопля фрі", Category = "Закуски", BasePrice = 59m }
        ]);
        categoryRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(
        [
            new ProductCategory { Id = 1, Name = "Закуски", SortOrder = 0 },
            new ProductCategory { Id = 2, Name = "Соуси", SortOrder = 1 }
        ]);
        bannerRepo.Setup(r => r.GetActiveAsync()).ReturnsAsync(
        [
            new MenuBanner { Id = 1, Title = "Акція", IsActive = true }
        ]);
        auth.Setup(a => a.HasSavedCredentialsAsync()).ReturnsAsync(false);
        session.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(false);

        var sut = new MenuViewModel(
            productRepo.Object,
            cartRepo.Object,
            orderRepo.Object,
            appState.Object,
            auth.Object,
            session.Object,
            nav.Object,
            dialog.Object,
            dispatcher,
            categoryRepository: categoryRepo.Object,
            bannerRepository: bannerRepo.Object);

        await sut.LoadCommand.ExecuteAsync(null);
        sut.HasBanners.Should().BeTrue();
        sut.CategoryChips.Select(c => c.Name).Should().Contain(["Усі категорії", "Закуски", "Соуси"]);

        await sut.OpenAdminCategoriesCommand.ExecuteAsync(null);
        await sut.OpenAdminBannersCommand.ExecuteAsync(null);
        dialog.Verify(d => d.AlertAsync("Доступ заборонено", It.IsAny<string>(), "OK"), Times.Exactly(2));

        session.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(true);
        await sut.OpenAdminCategoriesCommand.ExecuteAsync(null);
        await sut.OpenAdminBannersCommand.ExecuteAsync(null);
        nav.Verify(n => n.GoToAsync("admincategories", null), Times.Once);
        nav.Verify(n => n.GoToAsync("adminbanners", null), Times.Once);
    }
}
