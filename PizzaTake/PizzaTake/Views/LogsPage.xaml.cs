using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class LogsPage : ContentPage
{
    private readonly LogsViewModel viewModel;

    public LogsPage(LogsViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.LoadLogsCommand.Execute(null);
    }
}
