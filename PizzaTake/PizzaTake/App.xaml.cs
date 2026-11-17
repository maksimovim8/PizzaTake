using PizzaTake.Services;

namespace PizzaTake;

public partial class App : Application
{
    private readonly SqliteDatabase database;
    private readonly AppShell shell;

    public App(SqliteDatabase database, AppShell shell)
    {
        this.database = database;
        this.shell = shell;
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        _ = database.InitializeAsync();
        return new Window(shell);
    }
}
