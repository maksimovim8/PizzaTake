using FluentAssertions;
using Xunit;

namespace PizzaTake.UiTests;

/// <summary>
/// End-to-end Appium UI tests covering authentication and address creation via AutomationId selectors.
/// Requires a running Appium 2 server and Android emulator/device with PizzaTake installed.
/// </summary>
[Trait("Category", "UI")]
public sealed class PizzaTakeUiTests : IClassFixture<AppiumDriverFixture>
{
    private readonly AppiumDriverFixture fixture;

    public PizzaTakeUiTests(AppiumDriverFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void LoginFlow_ShouldAuthenticateAndOpenMainInterface()
    {
        fixture.CompleteOnboardingIfNeeded();

        // Navigate to Settings tab and open AuthPage.
        fixture.WaitForText("Налаштування").Click();
        fixture.WaitForAutomationId("SettingsChangePhoneButton").Click();

        // Enter phone number, accept policy, and request SMS OTP.
        var phoneEntry = fixture.WaitForAutomationId("AuthPhoneEntry");
        phoneEntry.Clear();
        phoneEntry.SendKeys("+380991112233");

        fixture.WaitForAutomationId("AuthPolicyCheckBox").Click();
        fixture.WaitForAutomationId("AuthRequestSmsButton").Click();

        // Enter valid 4-digit OTP and confirm.
        var otpEntry = fixture.WaitForAutomationId("AuthOtpEntry");
        otpEntry.SendKeys("1234");
        fixture.WaitForAutomationId("AuthVerifySmsButton").Click();

        // Verify authenticated main UI (Settings phone display or Menu search bar).
        var menuOrProfile = fixture.TryFindByAutomationId("MenuSearchBar", TimeSpan.FromSeconds(10))
            ?? fixture.WaitForAutomationId("SettingsMaskedPhoneLabel", TimeSpan.FromSeconds(10));

        menuOrProfile.Displayed.Should().BeTrue();
    }

    [Fact]
    public void CreateAddressFlow_ShouldSaveNewAddressAndDisplayInSettings()
    {
        fixture.CompleteOnboardingIfNeeded();

        // Open Settings and tap "+ Додати" to create a new saved address.
        fixture.WaitForText("Налаштування").Click();
        fixture.WaitForAutomationId("SettingsAddAddressButton").Click();

        const string addressLabel = "Офіс QA";
        const string addressText = "вул. Хрещатик, 22";

        var labelEntry = fixture.WaitForAutomationId("AddressLabelEntry");
        labelEntry.Clear();
        labelEntry.SendKeys(addressLabel);

        var textEntry = fixture.WaitForAutomationId("AddressTextEntry");
        textEntry.Clear();
        textEntry.SendKeys(addressText);

        var notesEntry = fixture.WaitForAutomationId("AddressNotesEntry");
        notesEntry.Clear();
        notesEntry.SendKeys("Під'їзд 1");

        fixture.WaitForAutomationId("AddressSaveButton").Click();

        // Verify the newly created address appears on SettingsPage.
        var savedAddressLabel = fixture.WaitForText(addressLabel, TimeSpan.FromSeconds(10));
        var savedAddressText = fixture.WaitForText(addressText, TimeSpan.FromSeconds(10));

        savedAddressLabel.Displayed.Should().BeTrue();
        savedAddressText.Displayed.Should().BeTrue();
    }
}
