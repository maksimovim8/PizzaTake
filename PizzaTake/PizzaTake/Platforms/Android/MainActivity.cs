using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using PizzaTake.Services;
using Serilog;
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
        SyncStatusBarForCurrentTheme();
        if (Microsoft.Maui.Controls.Application.Current is not null)
            Microsoft.Maui.Controls.Application.Current.RequestedThemeChanged += OnAppThemeChanged;
        HandleDeepLink(Intent);
    }

    protected override void OnResume()
    {
        base.OnResume();
        SyncStatusBarForCurrentTheme();
    }

    protected override void OnDestroy()
    {
        if (Microsoft.Maui.Controls.Application.Current is not null)
            Microsoft.Maui.Controls.Application.Current.RequestedThemeChanged -= OnAppThemeChanged;
        base.OnDestroy();
    }

    private void OnAppThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(SyncStatusBarForCurrentTheme);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent is not null)
            Intent = intent;
        HandleDeepLink(intent);
    }

    private void SyncStatusBarForCurrentTheme()
    {
        if (Window is null)
            return;

        var isDark = Microsoft.Maui.Controls.Application.Current?.RequestedTheme == AppTheme.Dark;
        var themeColor = Android.Graphics.Color.ParseColor(isDark ? "#181411" : "#F9F7F2");
        Window.SetBackgroundDrawable(new Android.Graphics.Drawables.ColorDrawable(themeColor));
#pragma warning disable CA1422
        Window.SetStatusBarColor(themeColor);
        Window.SetNavigationBarColor(themeColor);
#pragma warning restore CA1422
        var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);
        if (controller is not null)
        {
            controller.AppearanceLightStatusBars = !isDark;
            controller.AppearanceLightNavigationBars = !isDark;
        }
    }

    private static void HandleDeepLink(Intent? intent)
    {
        var data = intent?.Data;
        if (data is null || !string.Equals(data.Scheme, "pizzaapp", StringComparison.OrdinalIgnoreCase))
            return;

        var route = ParseDeepLinkRoute(data);
        if (string.IsNullOrWhiteSpace(route))
        {
            var rawId = data.GetQueryParameter("id");
            Log.ForContext<MainActivity>().Warning("Ignored deep link '{Uri}' with invalid id '{RawId}'", data.ToString(), rawId);
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (Shell.Current is not null)
                    await Shell.Current.DisplayAlertAsync("Посилання недійсне", "Некоректне посилання на товар або замовлення.", "OK");
            });
            return;
        }

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
                {
                    Log.ForContext<MainActivity>().Information("Navigating via deep link to route {Route}", route);
                    await appShell.TryNavigatePendingDeepLinkAsync();
                }
            }
            catch (Exception exception)
            {
                Log.ForContext<MainActivity>().Error(exception, "Deep link navigation failed for route {Route}", route);
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
