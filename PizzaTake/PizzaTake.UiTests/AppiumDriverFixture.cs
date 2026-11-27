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
        options.AddAdditionalAppiumOption("appium:appWaitActivity", "*");
        options.AddAdditionalAppiumOption("appium:autoGrantPermissions", true);
        options.AddAdditionalAppiumOption("appium:noReset", false);
        options.AddAdditionalAppiumOption("appium:newCommandTimeout", 120);
        options.AddAdditionalAppiumOption("appium:disableWindowAnimation", true);
        options.AddAdditionalAppiumOption("appium:unicodeKeyboard", true);
        options.AddAdditionalAppiumOption("appium:resetKeyboard", true);
        options.AddAdditionalAppiumOption("appium:settings[waitForIdleTimeout]", 0);
        options.AddAdditionalAppiumOption("appium:settings[enableMultiWindows]", true);

        if (!string.IsNullOrWhiteSpace(apkPath))
            options.App = apkPath;

        Driver = new AndroidDriver(new Uri(serverUrl), options, TimeSpan.FromSeconds(90));
    }

    public AppiumElement WaitForAutomationId(string automationId, TimeSpan? timeout = null)
    {
        var wait = new WebDriverWait(Driver, timeout ?? DefaultTimeout);
        wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException), typeof(NoSuchElementException));
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

    public void ClickAutomationId(string automationId, TimeSpan? timeout = null)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                WaitForAutomationId(automationId, timeout).Click();
                return;
            }
            catch (StaleElementReferenceException) when (attempt == 0)
            {
            }
        }
    }

    public void TypeText(string automationId, string text, bool clearFirst = false, TimeSpan? timeout = null)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                if (clearFirst)
                    WaitForAutomationId(automationId, timeout).Clear();

                WaitForAutomationId(automationId, timeout).SendKeys(text);
                return;
            }
            catch (StaleElementReferenceException) when (attempt == 0)
            {
            }
        }
    }

    /// <summary>
    /// Focuses an input and types via UiAutomator2 <c>mobile: type</c> without attempting to clear
    /// immutable prefixes (such as <c>+380</c> enforced by <c>PhoneInputBehavior</c>).
    /// </summary>
    public void TypeViaIme(string automationId, string text, TimeSpan? timeout = null)
    {
        ClickAutomationId(automationId, timeout);
        Driver.ExecuteScript("mobile: type", new Dictionary<string, object> { ["text"] = text });
    }

    public void TryHideKeyboard()
    {
        try
        {
            Driver.HideKeyboard();
        }
        catch
        {
            // Soft keyboard may already be hidden.
        }
    }

    public AppiumElement WaitForText(string text, TimeSpan? timeout = null)
    {
        var wait = new WebDriverWait(Driver, timeout ?? DefaultTimeout);
        wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException), typeof(NoSuchElementException));
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
        ClickAutomationId("PizzeriaConfirmButton", TimeSpan.FromSeconds(10));
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
