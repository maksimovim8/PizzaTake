using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PizzaTake.Core.Services;

namespace PizzaTake;

public partial class App : Application
{
    private readonly SqliteDatabase database;
    private readonly AppShell shell;
    private readonly IAppStateService appState;
    private readonly IServiceProvider services;
    private readonly ILogger<App> logger;

    public App(
        SqliteDatabase database,
        AppShell shell,
        IAppStateService appState,
        IServiceProvider services,
        ILogger<App> logger)
    {
        this.database = database;
        this.shell = shell;
        this.appState = appState;
        this.services = services;
        this.logger = logger;
        InitializeComponent();
        this.appState.RestoreState();
        this.logger.LogInformation("PizzaTake application initialized");
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        _ = InitializeDatabaseSafeAsync();
        var window = new Window(shell);
        window.Created += (_, _) =>
        {
            services.GetService<ISyncService>()?.StartConnectivityListener();
            logger.LogInformation("Application window created and started");
        };
        window.Activated += (_, _) =>
        {
            logger.LogInformation("App lifecycle event: Window.Activated");
        };
        window.Deactivated += (_, _) =>
        {
            logger.LogInformation("App lifecycle event: Window.Deactivated");
        };
        window.Stopped += (_, _) =>
        {
            appState.SaveState();
            services.GetService<ISyncService>()?.StopConnectivityListener();
            services.GetService<IShakeSensorService>()?.Stop();
            logger.LogInformation("Lifecycle: Application stopped; session and form draft state saved.");
        };
        window.Resumed += (_, _) =>
        {
            appState.RestoreState();
            services.GetService<ISyncService>()?.StartConnectivityListener();
            logger.LogInformation("Lifecycle: Application resumed; session and form draft state restored.");
        };
        window.Destroying += (_, _) =>
        {
            appState.SaveState();
            services.GetService<ISyncService>()?.StopConnectivityListener();
            services.GetService<IShakeSensorService>()?.Stop();
            logger.LogInformation("Lifecycle: Application window destroying; resources released.");
        };
        return window;
    }

    private async Task InitializeDatabaseSafeAsync()
    {
        try
        {
            await database.InitializeAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database initialization failed during application startup");
            services.GetService<ICrashReportingService>()?.CaptureException(ex, "App.InitializeDatabase");
        }
    }
}
