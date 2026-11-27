using PizzaTake.Core.Services;

namespace PizzaTake.Services.Platform;

public sealed class MauiThemeService : IThemeService
{
    public AppThemePreference CurrentTheme { get; private set; } = AppThemePreference.System;

    public void ApplyTheme(AppThemePreference theme)
    {
        CurrentTheme = theme;

        if (Application.Current is not null)
        {
            Application.Current.UserAppTheme = theme switch
            {
                AppThemePreference.Dark => AppTheme.Dark,
                AppThemePreference.Light => AppTheme.Light,
                _ => AppTheme.Unspecified
            };
        }

        var effectiveDark = theme switch
        {
            AppThemePreference.Dark => true,
            AppThemePreference.Light => false,
            _ => Application.Current?.RequestedTheme == AppTheme.Dark
        };

        ApplyPlatformStatusBar(effectiveDark);
    }

    private static void ApplyPlatformStatusBar(bool isDark)
    {
#if ANDROID
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            var window = activity?.Window;
            if (window is null)
                return;

            var statusBarColor = Android.Graphics.Color.ParseColor(isDark ? "#181411" : "#F9F7F2");
#pragma warning disable CA1422
            window.SetStatusBarColor(statusBarColor);
#pragma warning restore CA1422
            var controller = AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView);
            if (controller is not null)
                controller.AppearanceLightStatusBars = !isDark;
        });
#endif
    }
}
