using PizzaTake.Core.ViewModels;

namespace PizzaTake.Views;

public partial class AdminBannersPage : ContentPage
{
    private readonly AdminBannersViewModel viewModel;

    public AdminBannersPage(AdminBannersViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.LoadCommand.Execute(null);
    }
}
