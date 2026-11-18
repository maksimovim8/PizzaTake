using System.Diagnostics;
using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class CartPage : ContentPage
{
    private readonly CartViewModel viewModel;

    public CartPage(CartViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.LoadCartCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Debug.WriteLine("[Navigation] Leaving CartPage");
    }
}
