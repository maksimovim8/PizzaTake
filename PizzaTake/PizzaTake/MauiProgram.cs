using Microsoft.Extensions.Logging;
using PizzaTake.Services;
using PizzaTake.ViewModels;
using PizzaTake.Views;

namespace PizzaTake
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            SQLitePCL.Batteries_V2.Init();
            builder.Services.AddSingleton<SqliteDatabase>();
            builder.Services.AddSingleton<AppShell>();
            builder.Services.AddTransient<WelcomePage>();
            builder.Services.AddTransient<CitySelectionPage>();
            builder.Services.AddTransient<PizzeriaSelectionPage>();
            builder.Services.AddTransient<MenuPage>();
            builder.Services.AddTransient<PizzaDetailPage>();
            builder.Services.AddTransient<FavoritesPage>();
            builder.Services.AddTransient<CartPage>();
            builder.Services.AddTransient<AuthPage>();
            builder.Services.AddTransient<CheckoutPage>();
            builder.Services.AddTransient<OrderHistoryPage>();
            builder.Services.AddTransient<OrderDetailPage>();
            builder.Services.AddTransient<SettingsPage>();
            builder.Services.AddTransient<AddressEditPage>();
            builder.Services.AddTransient<WelcomeViewModel>();
            builder.Services.AddTransient<CitySelectionViewModel>();
            builder.Services.AddTransient<PizzeriaSelectionViewModel>();
            builder.Services.AddTransient<MenuViewModel>();
            builder.Services.AddTransient<PizzaDetailViewModel>();
            builder.Services.AddTransient<FavoritesViewModel>();
            builder.Services.AddTransient<CartViewModel>();
            builder.Services.AddTransient<AuthViewModel>();
            builder.Services.AddTransient<CheckoutViewModel>();
            builder.Services.AddTransient<OrderHistoryViewModel>();
            builder.Services.AddTransient<OrderDetailViewModel>();
            builder.Services.AddTransient<SettingsViewModel>();
            builder.Services.AddTransient<AddressEditViewModel>();

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
