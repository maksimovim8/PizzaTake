using PizzaTake.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace PizzaTake;

public partial class App : Application
{
    private readonly SqliteDatabase database;
    private readonly AppShell shell;
    private readonly IAppStateService appState;
    private readonly IAuthService authService;
    private readonly IServiceProvider services;

    public App(SqliteDatabase database, AppShell shell, IAppStateService appState, IAuthService authService, IServiceProvider services)
    {
        this.database = database;
        this.shell = shell;
        this.appState = appState;
        this.authService = authService;
        this.services = services;
        InitializeComponent();
        this.appState.RestoreState();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        _ = database.InitializeAsync();
        var window = new Window(shell);
        window.Created += async (_, _) =>
        {
            if (authService.IsAuthenticated)
                return;

            try
            {
                await shell.GoToAsync("welcome");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not open WelcomePage: {ex.Message}");
            }
        };
        window.Stopped += (_, _) =>
        {
            appState.SaveState();
            services.GetService<ISyncService>()?.StopConnectivityListener();
            Debug.WriteLine("Application stopped; state saved.");
        };
        window.Resumed += (_, _) =>
        {
            appState.RestoreState();
            services.GetService<ISyncService>()?.StartConnectivityListener();
            Debug.WriteLine("Application resumed; state restored.");
        };
        window.Destroying += (_, _) =>
        {
            services.GetService<ISyncService>()?.StopConnectivityListener();
            Debug.WriteLine("Application window destroyed.");
        };
        return window;
    }
}
