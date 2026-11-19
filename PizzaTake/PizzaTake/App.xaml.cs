using PizzaTake.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace PizzaTake;

public partial class App : Application
{
    private readonly SqliteDatabase database;
    private readonly AppShell shell;
    private readonly IAppStateService appState;
    private readonly IServiceProvider services;

    public App(SqliteDatabase database, AppShell shell, IAppStateService appState, IServiceProvider services)
    {
        this.database = database;
        this.shell = shell;
        this.appState = appState;
        this.services = services;
        InitializeComponent();
        this.appState.RestoreState();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        _ = database.InitializeAsync();
        var window = new Window(shell);
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
