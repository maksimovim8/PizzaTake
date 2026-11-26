using FluentAssertions;
using Moq;
using PizzaTake.Core.Services;
using PizzaTake.Core.ViewModels;
using Xunit;

namespace PizzaTake.Core.Tests.Unit;

public sealed class AuthViewModelTests
{
    private readonly Mock<IAuthService> authService = new();
    private readonly Mock<IBiometricAuthService> biometricService = new();
    private readonly Mock<INavigationService> navigation = new();

    private AuthViewModel CreateSut() => new(authService.Object, biometricService.Object, navigation.Object);

    [Theory]
    [InlineData("+380", false, false)]
    [InlineData("+380", true, false)]
    [InlineData("+38099123", true, false)]
    [InlineData("+380991234567", false, false)]
    [InlineData("+380991234567", true, true)]
    [InlineData("0990000001", true, true)]
    public void RequestSmsCommand_CanExecute_ShouldValidatePhoneAndPolicyAcceptance(
        string phone,
        bool policyAccepted,
        bool expectedCanExecute)
    {
        var sut = CreateSut();
        sut.PhoneInput = phone;
        sut.IsPolicyAccepted = policyAccepted;

        sut.RequestSmsCommand.CanExecute(null).Should().Be(expectedCanExecute);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("123", false)]
    [InlineData("12345", false)]
    [InlineData("12ab", false)]
    [InlineData("1234", true)]
    public void VerifySmsCommand_CanExecute_ShouldRequireFourDigits(string otpCode, bool expectedCanExecute)
    {
        var sut = CreateSut();
        sut.SmsOtpCode = otpCode;

        sut.VerifySmsCommand.CanExecute(null).Should().Be(expectedCanExecute);
    }

    [Fact]
    public async Task VerifySmsCommand_WhenValidCodeAndNextRoute_ShouldAuthenticateAndNavigate()
    {
        authService.Setup(a => a.VerifyOtpAsync("+380990000001", "1234")).ReturnsAsync(true);
        var sut = CreateSut();
        sut.ApplyQueryAttributes(new Dictionary<string, object> { ["next"] = "checkout" });
        sut.PhoneInput = "+380990000001";
        sut.SmsOtpCode = "1234";

        await sut.VerifySmsCommand.ExecuteAsync(null);

        sut.IsSmsPopupVisible.Should().BeFalse();
        navigation.Verify(n => n.GoToAsync("../checkout", null), Times.Once);
    }

    [Theory]
    [InlineData("", "+380")]
    [InlineData("+38", "+380")]
    [InlineData("099 000-00-01", "+380990000001")]
    [InlineData("+380 (99) 123-45-67", "+380991234567")]
    [InlineData("+380991234567999", "+380991234567")]
    public void PhoneNumberNormalizer_ShouldEnforceUkrainianFormatAndMaxLength(string raw, string expected)
    {
        PhoneNumberNormalizer.Normalize(raw).Should().Be(expected);
    }
}
