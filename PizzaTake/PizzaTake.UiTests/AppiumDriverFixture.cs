using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Support.UI;

namespace PizzaTake.UiTests;

/// <summary>
/// Manages an Appium 2 (UiAutomator2) AndroidDriver session for end-to-end UI testing by AutomationId.
/// </summary>
public sealed class AppiumDriverFixture : IDisposable
{
    public const string AppPackage = "com.companyname.pizzatake";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    public AndroidDriver Driver { get; }

    public AppiumDriverFixture()
    {
        var serverUrl = Environment.GetEnvironmentVariable("APPIUM_SERVER_URL") ?? "http://127.0.0.1:4723";
        var deviceName = Environment.GetEnvironmentVariable("ANDROID_DEVICE_NAME") ?? "emulator-5554";
        var apkPath = Environment.GetEnvironmentVariable("APP_APK_PATH");

        var options = new AppiumOptions
        {
            AutomationName = "UiAutomator2",
            PlatformName = "Android",
            DeviceName = deviceName
        };

        options.AddAdditionalAppiumOption("appium:appPackage", AppPackage);
        options.AddAdditionalAppiumOption("appium:autoGrantPermissions", true);
        options.AddAdditionalAppiumOption("appium:noReset", false);
        options.AddAdditionalAppiumOption("appium:newCommandTimeout", 120);

        if (!string.IsNullOrWhiteSpace(apkPath))
            options.App = apkPath;

        Driver = new AndroidDriver(new Uri(serverUrl), options, TimeSpan.FromSeconds(90));
    }

    public AppiumElement WaitForAutomationId(string automationId, TimeSpan? timeout = null)
    {
        var wait = new WebDriverWait(Driver, timeout ?? DefaultTimeout);
        return wait.Until(_ => FindElementByAutomationIdOrNull(automationId))
            ?? throw new NoSuchElementException($"Element with AutomationId '{automationId}' was not found.");
    }

    public AppiumElement? TryFindByAutomationId(string automationId, TimeSpan timeout)
    {
        try
        {
            return WaitForAutomationId(automationId, timeout);
        }
        catch (WebDriverTimeoutException)
        {
            return null;
        }
    }

    public AppiumElement WaitForText(string text, TimeSpan? timeout = null)
    {
        var wait = new WebDriverWait(Driver, timeout ?? DefaultTimeout);
        var selector = $"new UiSelector().textContains(\"{text.Replace("\"", "\\\"")}\")";
        return wait.Until(_ =>
        {
            var matches = Driver.FindElements(MobileBy.AndroidUIAutomator(selector));
            return matches.Count > 0 ? matches[0] : null;
        }) ?? throw new NoSuchElementException($"Element with text '{text}' was not found.");
    }

    public void CompleteOnboardingIfNeeded()
    {
        var startButton = TryFindByAutomationId("WelcomeStartButton", TimeSpan.FromSeconds(5));
        if (startButton is null)
            return;

        startButton.Click();
        WaitForText("Київ", TimeSpan.FromSeconds(10)).Click();
        WaitForText("TT 78", TimeSpan.FromSeconds(10)).Click();
        WaitForAutomationId("PizzeriaConfirmButton", TimeSpan.FromSeconds(10)).Click();
        WaitForAutomationId("MenuSearchBar", TimeSpan.FromSeconds(15));
    }

    private AppiumElement? FindElementByAutomationIdOrNull(string automationId)
    {
        var byResourceId = Driver.FindElements(MobileBy.Id($"{AppPackage}:id/{automationId}"));
        if (byResourceId.Count > 0)
            return byResourceId[0];

        var byAccessibilityId = Driver.FindElements(MobileBy.AccessibilityId(automationId));
        if (byAccessibilityId.Count > 0)
            return byAccessibilityId[0];

        return null;
    }

    public void Dispose()
    {
        try
        {
            Driver.Quit();
        }
        catch
        {
            // Ignore shutdown errors when Appium session already closed.
        }
        Driver.Dispose();
    }
}
