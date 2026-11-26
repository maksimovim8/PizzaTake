using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class AdminProductEditPage : ContentPage
{
    private readonly AdminProductEditViewModel viewModel;

    public AdminProductEditPage(AdminProductEditViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.StartSessionMonitoring();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        viewModel.StopSessionMonitoring();
    }
}
