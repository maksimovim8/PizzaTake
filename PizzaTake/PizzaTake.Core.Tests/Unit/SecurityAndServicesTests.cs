using FluentAssertions;
using Moq;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;
using PizzaTake.Core.Tests.Helpers;
using PizzaTake.Core.ViewModels;
using Xunit;

namespace PizzaTake.Core.Tests.Unit;

public sealed class SecurityAndServicesTests
{
    [Fact]
    public void HttpApiClient_VerifySpoofedCertificateRejected_ShouldReturnTrue()
    {
        using var client = new HttpApiClient();
        client.VerifySpoofedCertificateRejected().Should().BeTrue();
    }

    [Theory]
    [InlineData("+380990000001", UserRole.Administrator)]
    [InlineData("+380991112233", UserRole.Customer)]
    public async Task AuthService_VerifyOtpAsync_ShouldAssignRoleAndPersistInSecureStore(string phone, UserRole expectedRole)
    {
        using var apiClient = new HttpApiClient();
        var profileRepo = new Mock<IUserProfileRepository>();
        profileRepo.Setup(r => r.GetCurrentAsync()).ReturnsAsync((UserProfile?)null);
        var secureStore = new InMemorySecureStore();
        var session = new SessionService(secureStore);
        var auth = new AuthService(apiClient, profileRepo.Object, secureStore, session);

        var verified = await auth.VerifyOtpAsync(phone, "1234");

        verified.Should().BeTrue();
        session.IsAuthenticated.Should().BeTrue();
        session.CurrentRole.Should().Be(expectedRole);
        (await secureStore.GetAsync("pizzatake_auth_token")).Should().NotBeNullOrWhiteSpace();
        (await secureStore.GetAsync("pizzatake_user_role")).Should().Be(expectedRole.ToString());
    }

    [Fact]
    public async Task AdminProductEditViewModel_WhenPromoPriceNotLessThanBasePrice_ShouldShowValidationError()
    {
        var productRepo = new Mock<IProductRepository>();
        var session = new Mock<ISessionService>();
        session.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(true);
        var sync = new Mock<ISyncService>();
        var nav = new Mock<INavigationService>();
        var dialog = new Mock<IDialogService>();
        var dispatcher = new ImmediateMainThreadDispatcher();

        var sut = new AdminProductEditViewModel(
            productRepo.Object, session.Object, sync.Object, nav.Object, dialog.Object, dispatcher);

        sut.Name = "Тестова Піца";
        sut.BasePriceInput = "250";
        sut.PromoPriceInput = "250";

        await sut.SaveProductCommand.ExecuteAsync(null);

        sut.HasError.Should().BeTrue();
        sut.ErrorMessage.Should().Contain("меншою за базову");
        productRepo.Verify(r => r.SaveAsync(It.IsAny<Product>()), Times.Never);
    }

    [Fact]
    public async Task AdminProductEditViewModel_WhenCustomerCallsSave_ShouldDenyAndNotSave()
    {
        var productRepo = new Mock<IProductRepository>();
        var session = new Mock<ISessionService>();
        session.Setup(s => s.IsInRole(UserRole.Administrator)).Returns(false);
        session.Setup(s => s.CurrentRole).Returns(UserRole.Customer);
        var sync = new Mock<ISyncService>();
        var nav = new Mock<INavigationService>();
        var dialog = new Mock<IDialogService>();
        var dispatcher = new ImmediateMainThreadDispatcher();

        var sut = new AdminProductEditViewModel(
            productRepo.Object, session.Object, sync.Object, nav.Object, dialog.Object, dispatcher);

        sut.Name = "Хакерська Піца";
        sut.BasePriceInput = "100";

        await sut.SaveProductCommand.ExecuteAsync(null);

        dialog.Verify(d => d.AlertAsync("Доступ заборонено", It.IsAny<string>(), "OK"), Times.Once);
        productRepo.Verify(r => r.SaveAsync(It.IsAny<Product>()), Times.Never);
    }

    [Fact]
    public async Task AuthService_LockSessionAsync_ShouldRemainLockedAfterRestartUntilRestored()
    {
        using var apiClient = new HttpApiClient();
        var profileRepo = new Mock<IUserProfileRepository>();
        profileRepo.Setup(r => r.GetCurrentAsync()).ReturnsAsync((UserProfile?)null);
        var secureStore = new InMemorySecureStore();
        var initialSession = new SessionService(secureStore);
        var auth = new AuthService(apiClient, profileRepo.Object, secureStore, initialSession);

        (await auth.VerifyOtpAsync("+380991112233", "1234")).Should().BeTrue();
        await auth.LockSessionAsync();

        // Simulate app restart with a fresh SessionService instance reading the same SecureStorage.
        var restartedSession = new SessionService(secureStore);
        await restartedSession.InitializeAsync();
        var restartedAuth = new AuthService(apiClient, profileRepo.Object, secureStore, restartedSession);

        restartedSession.IsAuthenticated.Should().BeFalse();
        restartedSession.CurrentRole.Should().BeNull();
        (await restartedAuth.HasSavedCredentialsAsync()).Should().BeTrue();

        // Unlocking via biometric/saved session clears the lock flag.
        (await restartedAuth.RestoreSavedSessionAsync()).Should().BeTrue();
        restartedSession.IsAuthenticated.Should().BeTrue();
        restartedSession.CurrentRole.Should().Be(UserRole.Customer);
    }
}
