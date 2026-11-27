using FluentAssertions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;
using Xunit;

namespace PizzaTake.Core.Tests.Integration;

public sealed class RepositoryCrudIntegrationTests : IAsyncLifetime
{
    private string dbPath = string.Empty;
    private SqliteDatabase database = null!;

    public async Task InitializeAsync()
    {
        SQLitePCL.Batteries_V2.Init();
        dbPath = Path.Combine(Path.GetTempPath(), $"pizzatake_crud_{Guid.NewGuid():N}.db3");
        database = new SqliteDatabase(dbPath);
        await database.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await database.CloseAsync();
        if (File.Exists(dbPath))
            File.Delete(dbPath);
    }

    [Fact]
    public async Task AddressRepository_FullCrudCycle_ShouldInsertReadUpdateAndDelete()
    {
        var repo = new AddressRepository(database);
        var address = new UserAddress
        {
            UserId = 1,
            Label = "Дім",
            Text = "вул. Хрещатик, 22",
            Notes = "Під'їзд 2",
            Latitude = 50.4477,
            Longitude = 30.5228
        };

        await repo.SaveAsync(address);
        address.Id.Should().BeGreaterThan(0);

        var loaded = await repo.GetByIdAsync(address.Id);
        loaded.Should().NotBeNull();
        loaded!.Text.Should().Be("вул. Хрещатик, 22");
        loaded.Notes.Should().Be("Під'їзд 2");

        loaded.Text = "вул. Хрещатик, 24, кв. 5";
        await repo.SaveAsync(loaded);

        var updated = await repo.GetByIdAsync(address.Id);
        updated!.Text.Should().Be("вул. Хрещатик, 24, кв. 5");

        await repo.DeleteAsync(updated);
        var deleted = await repo.GetByIdAsync(address.Id);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task OrderRepository_SaveUpdateStatusAndDelete_ShouldPersistTransactionally()
    {
        var repo = new OrderRepository(database);
        var order = new Order
        {
            OrderNumber = "PD-7777",
            UserId = 1,
            PizzeriaId = 1,
            Status = OrderStatus.Preparing,
            DeliveryType = DeliveryType.Delivery,
            PaymentMethod = PaymentMethod.CardOnline,
            DeliveryAddress = "вул. Сагайдачного, 10",
            TotalAmount = 518m,
            CreatedAtUtc = DateTime.UtcNow
        };
        var items = new[]
        {
            new OrderItem { ProductId = 1, ProductName = "Маргарита", Quantity = 2, UnitPrice = 259m, SizeName = "Середня" }
        };

        await repo.SaveOrderWithItemsAsync(order, items);
        order.Id.Should().BeGreaterThan(0);

        var storedOrder = await repo.GetByIdAsync(order.Id);
        storedOrder.Should().NotBeNull();
        storedOrder!.OrderNumber.Should().Be("PD-7777");

        var storedItems = await repo.GetItemsByOrderIdAsync(order.Id);
        storedItems.Should().ContainSingle(i => i.ProductName == "Маргарита" && i.Quantity == 2);

        await repo.UpdateStatusAsync(order.Id, OrderStatus.Completed);
        (await repo.GetByIdAsync(order.Id))!.Status.Should().Be(OrderStatus.Completed);

        await repo.DeleteOrderAsync(order.Id);
        (await repo.GetByIdAsync(order.Id)).Should().BeNull();
        (await repo.GetItemsByOrderIdAsync(order.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task QualityReportRepository_FullCrudCycle_ShouldPersistQueryByOrderAndDelete()
    {
        var repo = new QualityReportRepository(database);
        var report = new QualityReport
        {
            OrderId = 42,
            OrderNumber = "PD-4242",
            Description = "Коробка була відкрита при доставці",
            PhotoPath = "/app/photos/report_42.jpg",
            Latitude = 50.4501,
            Longitude = 30.5234,
            CreatedAtUtc = DateTime.UtcNow
        };

        await repo.SaveAsync(report);
        report.Id.Should().BeGreaterThan(0);

        var byId = await repo.GetByIdAsync(report.Id);
        byId.Should().NotBeNull();
        byId!.Description.Should().Be("Коробка була відкрита при доставці");
        byId.HasPhoto.Should().BeTrue();
        byId.HasCoordinates.Should().BeTrue();

        var byOrder = await repo.GetByOrderIdAsync(42);
        byOrder.Should().ContainSingle(r => r.Id == report.Id);

        var all = await repo.GetAllAsync();
        all.Should().Contain(r => r.Id == report.Id);

        await repo.DeleteAsync(byId);
        (await repo.GetByIdAsync(report.Id)).Should().BeNull();
    }

    [Fact]
    public async Task CategoryBannerAndProductOptionRepositories_ShouldSupportFullCrudAndSeededUpsells()
    {
        var productRepo = new ProductRepository(database);
        var categoryRepo = new CategoryRepository(database);
        var bannerRepo = new MenuBannerRepository(database);

        var products = await productRepo.GetAllAsync();
        products.Should().Contain(p => p.Category == "Закуски" && p.Name == "Картопля фрі");
        products.Should().Contain(p => p.Category == "Соуси" && p.Name == "Соус BBQ");

        var option = new IngredientOption { ProductId = products[0].Id, Name = "Трюфельний сир", Price = 49m, Kind = OptionKind.AddOn };
        await productRepo.SaveOptionAsync(option);
        option.Id.Should().BeGreaterThan(0);
        (await productRepo.GetOptionsByProductIdAsync(products[0].Id)).Should().Contain(o => o.Id == option.Id);
        await productRepo.DeleteOptionAsync(option);
        (await productRepo.GetOptionsByProductIdAsync(products[0].Id)).Should().NotContain(o => o.Id == option.Id);

        var cat = new ProductCategory { Name = "Комбо", SortOrder = 10 };
        await categoryRepo.SaveAsync(cat);
        await categoryRepo.RenameAsync(cat, "Комбо-набори");
        (await categoryRepo.GetAllAsync()).Should().Contain(c => c.Name == "Комбо-набори");
        await categoryRepo.DeleteAsync(cat);
        (await categoryRepo.GetAllAsync()).Should().NotContain(c => c.Name == "Комбо-набори");

        var banner = new MenuBanner { Eyebrow = "ТЕСТ", Title = "Новий банер", Subtitle = "Опис", BadgeText = "199 ₴", IsActive = true, SortOrder = 5 };
        await bannerRepo.SaveAsync(banner);
        (await bannerRepo.GetActiveAsync()).Should().Contain(b => b.Id == banner.Id);
        await bannerRepo.DeleteAsync(banner);
        (await bannerRepo.GetAllAsync()).Should().NotContain(b => b.Id == banner.Id);
    }
}
