using PizzaTake.Core.ViewModels;

namespace PizzaTake.Views;

public partial class AdminCategoriesPage : ContentPage
{
    private readonly AdminCategoriesViewModel viewModel;

    public AdminCategoriesPage(AdminCategoriesViewModel viewModel)
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
