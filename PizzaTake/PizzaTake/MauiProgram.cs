using Microsoft.Extensions.Logging;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;
using PizzaTake.Core.ViewModels;
using PizzaTake.Services.Platform;
using PizzaTake.Views;
using Sentry;
using Serilog;
using Serilog.Events;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace PizzaTake
{
    public static class MauiProgram
    {
        private const string LogOutputTemplate =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

        public static MauiApp CreateMauiApp()
        {
            ConfigureSerilog();
            GlobalExceptionHandler.Register();

            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseSkiaSharp()
                .UseSentry(options =>
                {
                    options.Dsn = ResolveSentryDsn();
                    options.SendDefaultPii = false;
                    options.TracesSampleRate = 1.0;
#if DEBUG
                    options.Debug = true;
                    options.Environment = "development";
#else
                    options.Environment = "production";
#endif
                })
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                })
                .ConfigureMauiHandlers(handlers =>
                {
#if ANDROID
                    handlers.AddHandler<AppShell, PizzaTakeShellRenderer>();
#endif
                });

            builder.Logging.ClearProviders();
            builder.Logging.AddSerilog(Log.Logger, dispose: false);

            SQLitePCL.Batteries_V2.Init();
            builder.Services.AddSingleton<IAppPathsService, MauiAppPathsService>();
            builder.Services.AddSingleton<ISettingsService, MauiSettingsService>();
            builder.Services.AddSingleton<IThemeService, MauiThemeService>();
            builder.Services.AddSingleton<INavigationService, MauiNavigationService>();
            builder.Services.AddSingleton<IDialogService, MauiDialogService>();
            builder.Services.AddSingleton<IMainThreadDispatcher, MauiMainThreadDispatcher>();

            builder.Services.AddSingleton(sp => new SqliteDatabase(
                sp.GetRequiredService<IAppPathsService>(),
                sp.GetService<ILogger<SqliteDatabase>>()));
            builder.Services.AddSingleton<IAppStateService, AppStateService>();
            builder.Services.AddSingleton<ICityRepository, CityRepository>();
            builder.Services.AddSingleton<IPizzeriaRepository, PizzeriaRepository>();
            builder.Services.AddSingleton<IProductRepository, ProductRepository>();
            builder.Services.AddSingleton<ICategoryRepository, CategoryRepository>();
            builder.Services.AddSingleton<IMenuBannerRepository, MenuBannerRepository>();
            builder.Services.AddSingleton<IFavoriteRepository, FavoriteRepository>();
            builder.Services.AddSingleton<ICartRepository, CartRepository>();
            builder.Services.AddSingleton<IOrderRepository, OrderRepository>();
            builder.Services.AddSingleton<IUserProfileRepository, UserProfileRepository>();
            builder.Services.AddSingleton<IAddressRepository, AddressRepository>();
            builder.Services.AddSingleton<ISyncQueueRepository, SyncQueueRepository>();
            builder.Services.AddSingleton<IQualityReportRepository, QualityReportRepository>();
            builder.Services.AddSingleton<ISecureStoreService, MauiSecureStoreService>();
            builder.Services.AddSingleton<ISessionService, SessionService>();
            builder.Services.AddSingleton<IBiometricAuthService, MauiBiometricAuthService>();
            builder.Services.AddSingleton<IEncryptionService, AesEncryptionService>();
            builder.Services.AddSingleton<IPermissionService, MauiPermissionService>();
            builder.Services.AddSingleton<IMediaService, MauiMediaService>();
            builder.Services.AddSingleton<ILocationService, MauiLocationService>();
            builder.Services.AddSingleton<IMapLauncherService, MauiMapLauncherService>();
            builder.Services.AddSingleton<IShakeSensorService, MauiShakeSensorService>();
            builder.Services.AddSingleton<IConnectivityService, MauiConnectivityService>();
            builder.Services.AddSingleton(sp => new HttpApiClient(sp.GetService<ILogger<HttpApiClient>>()));
            builder.Services.AddSingleton<ISyncService, SyncService>();
            builder.Services.AddSingleton<IAuthService, AuthService>();
            builder.Services.AddSingleton<IUserDataResetService, UserDataResetService>();
            builder.Services.AddSingleton<ICrashReportingService, SentryCrashReportingService>();
            builder.Services.AddSingleton<ILogReaderService, SerilogLogReaderService>();
            builder.Services.AddSingleton<IBugReportService, BugReportService>();
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
            builder.Services.AddTransient<AdminProductEditPage>();
            builder.Services.AddTransient<AdminCategoriesPage>();
            builder.Services.AddTransient<AdminBannersPage>();
            builder.Services.AddTransient<LogsPage>();
            builder.Services.AddTransient<QualityReportPage>();
            builder.Services.AddTransient<PizzeriasMapPage>();
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
            builder.Services.AddTransient<AdminProductEditViewModel>();
            builder.Services.AddTransient<AdminCategoriesViewModel>();
            builder.Services.AddTransient<AdminBannersViewModel>();
            builder.Services.AddTransient<LogsViewModel>();
            builder.Services.AddTransient<QualityReportViewModel>();
            builder.Services.AddTransient<PizzeriasMapViewModel>();

            Log.Information("PizzaTake MauiApp configured successfully");
            return builder.Build();
        }

        private static void ConfigureSerilog()
        {
            var logsDirectory = Path.Combine(FileSystem.AppDataDirectory, "logs");
            Directory.CreateDirectory(logsDirectory);
            var logFilePath = Path.Combine(logsDirectory, "app-.log");

            var loggerConfiguration = new LoggerConfiguration()
#if DEBUG
                .MinimumLevel.Debug()
#else
                .MinimumLevel.Information()
#endif
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Debug(outputTemplate: LogOutputTemplate)
                .WriteTo.File(
                    path: logFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    shared: true,
                    outputTemplate: LogOutputTemplate);

            Log.Logger = loggerConfiguration.CreateLogger();
        }

        private static string ResolveSentryDsn()
        {
            var envDsn = Environment.GetEnvironmentVariable("SENTRY_DSN");
            if (!string.IsNullOrWhiteSpace(envDsn))
                return envDsn.Trim();

            var assemblyDsn = typeof(MauiProgram).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), inherit: false)
                .OfType<System.Reflection.AssemblyMetadataAttribute>()
                .FirstOrDefault(attr => string.Equals(attr.Key, "SentryDsn", StringComparison.Ordinal))
                ?.Value;
            if (!string.IsNullOrWhiteSpace(assemblyDsn))
                return assemblyDsn.Trim();

            return Preferences.Default.Get("diagnostics.sentry_dsn", string.Empty).Trim();
        }
    }
}
