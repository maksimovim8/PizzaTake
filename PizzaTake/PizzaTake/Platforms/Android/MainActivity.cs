using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Maui.ApplicationModel;
using System.Diagnostics;
using System.Globalization;

namespace PizzaTake;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "pizzaapp", DataHost = "pizza")]
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
        var rawId = intent?.Data?.GetQueryParameter("id");
        if (!int.TryParse(rawId, NumberStyles.None, CultureInfo.InvariantCulture, out var productId) || productId <= 0)
            return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (Shell.Current is not null)
                    await Shell.Current.GoToAsync($"pizzadetail?id={productId}");
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Deep link navigation failed: {exception}");
            }
        });
    }
}
