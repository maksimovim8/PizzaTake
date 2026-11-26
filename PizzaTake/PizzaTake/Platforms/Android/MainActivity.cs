using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;
using Microsoft.Maui.ApplicationModel;
using Serilog;
using System.Globalization;

namespace PizzaTake;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "pizzaapp", DataHost = "pizza")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SyncStatusBarForCurrentTheme();
        HandleDeepLink(Intent);
    }

    protected override void OnResume()
    {
        base.OnResume();
        SyncStatusBarForCurrentTheme();
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
        var statusBarColor = Android.Graphics.Color.ParseColor(isDark ? "#181411" : "#F9F7F2");
#pragma warning disable CA1422
        Window.SetStatusBarColor(statusBarColor);
#pragma warning restore CA1422
        var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);
        if (controller is not null)
            controller.AppearanceLightStatusBars = !isDark;
    }

    private static void HandleDeepLink(Intent? intent)
    {
        var data = intent?.Data;
        if (data is null || !string.Equals(data.Scheme, "pizzaapp", StringComparison.OrdinalIgnoreCase))
            return;

        var rawId = data.GetQueryParameter("id");
        if (!int.TryParse(rawId, NumberStyles.None, CultureInfo.InvariantCulture, out var productId) || productId <= 0)
        {
            Log.ForContext<MainActivity>().Warning("Ignored deep link with invalid product id '{RawId}'", rawId);
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (Shell.Current is not null)
                    await Shell.Current.DisplayAlertAsync("Товар не знайдено", "Некоректне посилання на товар.", "OK");
            });
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (Shell.Current is not null)
                {
                    Log.ForContext<MainActivity>().Information("Navigating via deep link to product {ProductId}", productId);
                    await Shell.Current.GoToAsync($"pizzadetail?id={productId}");
                }
            }
            catch (Exception exception)
            {
                Log.ForContext<MainActivity>().Error(exception, "Deep link navigation failed for product {ProductId}", productId);
            }
        });
    }
}
