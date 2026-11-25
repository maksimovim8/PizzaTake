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
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        _ = database.InitializeAsync();
        services.GetService<ISyncService>()?.StartConnectivityListener();
        logger.LogInformation("Lifecycle: Application window created.");

        var window = new Window(shell);
        window.Stopped += (_, _) =>
        {
            appState.SaveState();
            services.GetService<ISyncService>()?.StopConnectivityListener();
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
            logger.LogInformation("Lifecycle: Application window destroying; resources released.");
        };
        return window;
    }
}
