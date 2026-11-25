using System.Diagnostics;
using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class MenuPage : ContentPage
{
    private readonly MenuViewModel viewModel;

    public MenuPage(MenuViewModel viewModel)
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

    protected override void OnDisappearing()
    {
        Debug.WriteLine("Leaving page: MenuPage");
        base.OnDisappearing();
    }
}
