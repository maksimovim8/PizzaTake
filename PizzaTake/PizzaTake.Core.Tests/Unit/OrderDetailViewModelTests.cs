using FluentAssertions;
using Moq;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;
using PizzaTake.Core.Tests.Helpers;
using PizzaTake.Core.ViewModels;
using Xunit;

namespace PizzaTake.Core.Tests.Unit;

public sealed class OrderDetailViewModelTests
{
    private readonly Mock<IOrderRepository> orderRepo = new();
    private readonly Mock<ICartRepository> cartRepo = new();
    private readonly Mock<IAuthService> authService = new();
    private readonly Mock<ISessionService> sessionService = new();
    private readonly Mock<ISyncService> syncService = new();
    private readonly Mock<INavigationService> navigation = new();
    private readonly Mock<IDialogService> dialogService = new();
    private readonly ImmediateMainThreadDispatcher dispatcher = new();

    private OrderDetailViewModel CreateSut() => new(
        orderRepo.Object,
        cartRepo.Object,
        authService.Object,
        sessionService.Object,
        syncService.Object,
        navigation.Object,
        dialogService.Object,
        dispatcher);

    [Fact]
    public async Task AdvanceAndDeleteOrder_WhenCustomer_ShouldDenyAccessAndNeverMutateRepository()
    {
        authService.Setup(a => a.HasSavedCredentialsAsync()).ReturnsAsync(true);
        sessionService.Setup(s => s.IsAuthenticated).Returns(true);
        sessionService.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(false);
        sessionService.Setup(s => s.CurrentRole).Returns(UserRole.Customer);

        var order = new Order { Id = 10, OrderNumber = "PD-1010", Status = OrderStatus.Preparing };
        orderRepo.Setup(r => r.GetItemsByOrderIdAsync(10)).ReturnsAsync([]);

        var sut = CreateSut();
        await sut.LoadOrderAsync(order);

        sut.IsAdmin.Should().BeFalse();
        sut.CanShowAdminPanel.Should().BeFalse();

        await sut.AdvanceOrderStatusCommand.ExecuteAsync(null);
        await sut.DeleteOrderCommand.ExecuteAsync(null);

        orderRepo.Verify(r => r.UpdateStatusAsync(It.IsAny<int>(), It.IsAny<OrderStatus>()), Times.Never);
        orderRepo.Verify(r => r.DeleteOrderAsync(It.IsAny<int>()), Times.Never);
        dialogService.Verify(d => d.AlertAsync("Доступ заборонено", It.IsAny<string>(), "OK"), Times.Exactly(2));
    }

    [Fact]
    public async Task AdvanceOrderStatus_WhenAdministrator_ShouldAdvanceStatusAndSkipDeliveringForPickup()
    {
        authService.Setup(a => a.HasSavedCredentialsAsync()).ReturnsAsync(true);
        sessionService.Setup(s => s.IsAuthenticated).Returns(true);
        sessionService.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(true);
        sessionService.Setup(s => s.CurrentRole).Returns(UserRole.Administrator);

        var order = new Order
        {
            Id = 20,
            OrderNumber = "PD-2020",
            Status = OrderStatus.Ready,
            DeliveryType = DeliveryType.Pickup
        };
        orderRepo.Setup(r => r.GetItemsByOrderIdAsync(20)).ReturnsAsync([]);

        var sut = CreateSut();
        await sut.LoadOrderAsync(order);

        sut.CanShowAdminPanel.Should().BeTrue();
        await sut.AdvanceOrderStatusCommand.ExecuteAsync(null);

        orderRepo.Verify(r => r.UpdateStatusAsync(20, OrderStatus.Completed), Times.Once);
        sut.CurrentOrder!.Status.Should().Be(OrderStatus.Completed);
        syncService.Verify(s => s.EnqueueAsync("Order", "UpdateStatus", It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task DeleteOrder_WhenAdministratorConfirms_ShouldDeleteAndNavigateBack()
    {
        authService.Setup(a => a.HasSavedCredentialsAsync()).ReturnsAsync(true);
        sessionService.Setup(s => s.IsAuthenticated).Returns(true);
        sessionService.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(true);
        dialogService.Setup(d => d.ConfirmAsync("Видалення замовлення", It.IsAny<string>(), "Видалити", "Скасувати"))
            .ReturnsAsync(true);

        var order = new Order { Id = 33, OrderNumber = "PD-3333", Status = OrderStatus.Preparing };
        orderRepo.Setup(r => r.GetItemsByOrderIdAsync(33)).ReturnsAsync([]);

        var sut = CreateSut();
        await sut.LoadOrderAsync(order);
        await sut.DeleteOrderCommand.ExecuteAsync(null);

        orderRepo.Verify(r => r.DeleteOrderAsync(33), Times.Once);
        syncService.Verify(s => s.EnqueueAsync("Order", "Delete", It.IsAny<object>()), Times.Once);
        navigation.Verify(n => n.GoBackAsync(null), Times.Once);
    }
}
