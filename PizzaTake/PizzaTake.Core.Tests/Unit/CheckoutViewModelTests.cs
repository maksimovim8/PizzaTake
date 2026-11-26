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

public sealed class CheckoutViewModelTests
{
    private readonly Mock<ICartRepository> cartRepo = new();
    private readonly Mock<IOrderRepository> orderRepo = new();
    private readonly Mock<IAddressRepository> addressRepo = new();
    private readonly Mock<IUserProfileRepository> profileRepo = new();
    private readonly Mock<ISyncService> syncService = new();
    private readonly Mock<IAppStateService> appState = new();
    private readonly Mock<INavigationService> navigation = new();
    private readonly InMemorySettingsService settings = new();
    private readonly Mock<IConnectivityService> connectivity = new();
    private readonly ImmediateMainThreadDispatcher dispatcher = new();
    private readonly Mock<ILogger<CheckoutViewModel>> logger = new();
    private readonly Mock<ICrashReportingService> crashReporting = new();
    private readonly Mock<IBugReportService> bugReportService = new();

    private CheckoutViewModel CreateSut() => new(
        cartRepo.Object,
        orderRepo.Object,
        addressRepo.Object,
        profileRepo.Object,
        syncService.Object,
        appState.Object,
        navigation.Object,
        settings,
        connectivity.Object,
        dispatcher,
        logger.Object,
        crashReporting.Object,
        bugReportService.Object);

    [Fact]
    public async Task ConfirmPaymentCommand_WhenValid_ShouldSaveOrderClearCartAndEnqueueSync()
    {
        connectivity.Setup(c => c.IsConnected).Returns(true);
        appState.Setup(s => s.SelectedPizzeria).Returns(new Pizzeria { Id = 1, StoreNumber = "TT 78" });
        var items = new List<CartItem>
        {
            new() { Id = 1, ProductId = 1, ProductName = "Чеддерман", Quantity = 2, UnitPrice = 219m }
        };
        cartRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(items);
        addressRepo.Setup(r => r.GetAllAsync()).ReturnsAsync([]);
        profileRepo.Setup(r => r.GetCurrentAsync()).ReturnsAsync(new UserProfile { Id = 5 });
        orderRepo
            .Setup(r => r.SaveOrderWithItemsAsync(It.IsAny<Order>(), It.IsAny<IEnumerable<OrderItem>>()))
            .Callback<Order, IEnumerable<OrderItem>>((o, _) => o.Id = 99)
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        await sut.LoadCheckoutCommand.ExecuteAsync(null);
        sut.DeliveryAddress = "вул. Хрещатик, 22";

        await sut.ConfirmPaymentCommand.ExecuteAsync(null);

        orderRepo.Verify(r => r.SaveOrderWithItemsAsync(
            It.Is<Order>(o => o.TotalAmount == 438m && o.PizzeriaId == 1),
            It.Is<IEnumerable<OrderItem>>(l => l.Count() == 1)), Times.Once);
        cartRepo.Verify(r => r.ClearAsync(), Times.Once);
        syncService.Verify(s => s.EnqueueAsync("Order", "Insert", It.IsAny<object>()), Times.Once);
        crashReporting.Verify(c => c.TrackEvent(CrashEventNames.OrderPlaced, It.IsAny<IDictionary<string, string>>()), Times.Once);
        navigation.Verify(n => n.GoToAsync("orderdetail?id=99", null), Times.Once);
    }

    [Fact]
    public async Task ConfirmPaymentCommand_WhenRepositoryThrows_ShouldLogErrorCaptureExceptionAndEnableBugReport()
    {
        connectivity.Setup(c => c.IsConnected).Returns(true);
        appState.Setup(s => s.SelectedPizzeria).Returns(new Pizzeria { Id = 1, StoreNumber = "TT 78" });
        cartRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(
        [
            new CartItem { Id = 1, ProductId = 1, ProductName = "Маргарита", Quantity = 1, UnitPrice = 259m }
        ]);
        addressRepo.Setup(r => r.GetAllAsync()).ReturnsAsync([]);
        profileRepo.Setup(r => r.GetCurrentAsync()).ReturnsAsync(new UserProfile { Id = 5 });
        orderRepo
            .Setup(r => r.SaveOrderWithItemsAsync(It.IsAny<Order>(), It.IsAny<IEnumerable<OrderItem>>()))
            .ThrowsAsync(new InvalidOperationException("SQLite disk full"));

        var sut = CreateSut();
        await sut.LoadCheckoutCommand.ExecuteAsync(null);
        sut.DeliveryAddress = "вул. Хрещатик, 22";

        await sut.ConfirmPaymentCommand.ExecuteAsync(null);

        sut.HasError.Should().BeTrue();
        sut.CanReportError.Should().BeTrue();
        cartRepo.Verify(r => r.ClearAsync(), Times.Never);
        crashReporting.Verify(c => c.CaptureException(It.IsAny<InvalidOperationException>(), "CheckoutViewModel.ConfirmPaymentAsync"), Times.Once);
        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
