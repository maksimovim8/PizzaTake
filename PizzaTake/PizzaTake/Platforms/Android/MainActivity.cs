using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using PizzaTake.Services;
using System.Diagnostics;
using System.Globalization;

namespace PizzaTake;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "pizzaapp", DataHost = "pizza")]
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "pizzaapp", DataHost = "order")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        HandleDeepLink(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent is not null)
            Intent = intent;
        HandleDeepLink(intent);
    }

    private static void HandleDeepLink(Intent? intent)
    {
        var route = ParseDeepLinkRoute(intent?.Data);
        if (string.IsNullOrWhiteSpace(route))
            return;

        var appState = IPlatformApplication.Current?.Services?.GetService<IAppStateService>();
        if (appState is not null)
            appState.PendingDeepLinkRoute = route;
        else
            AppShell.SetColdStartDeepLink(route);

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (Shell.Current is AppShell appShell)
                    await appShell.TryNavigatePendingDeepLinkAsync();
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Deep link navigation failed: {exception}");
            }
        });
    }

    private static string? ParseDeepLinkRoute(Android.Net.Uri? data)
    {
        if (data is null)
            return null;

        var rawId = data.GetQueryParameter("id");
        if (!int.TryParse(rawId, NumberStyles.None, CultureInfo.InvariantCulture, out var entityId) || entityId <= 0)
            return null;

        return data.Host?.ToLowerInvariant() switch
        {
            "pizza" => $"pizzadetail?id={entityId}",
            "order" => $"orderdetail?id={entityId}",
            _ => null
        };
    }
}
