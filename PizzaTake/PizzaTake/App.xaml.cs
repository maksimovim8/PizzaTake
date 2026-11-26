using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PizzaTake.Services;

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
        window.Stopped += (_, _) =>
        {
            appState.SaveState();
            services.GetService<ISyncService>()?.StopConnectivityListener();
            logger.LogInformation("Application stopped; state saved");
        };
        window.Resumed += (_, _) =>
        {
            appState.RestoreState();
            services.GetService<ISyncService>()?.StartConnectivityListener();
            logger.LogInformation("Application resumed; state restored");
        };
        window.Destroying += (_, _) =>
        {
            services.GetService<ISyncService>()?.StopConnectivityListener();
            logger.LogInformation("Application window destroying");
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
