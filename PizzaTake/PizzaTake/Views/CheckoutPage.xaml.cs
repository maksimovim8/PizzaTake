using System.Diagnostics;
using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class CheckoutPage : ContentPage
{
    private readonly CheckoutViewModel viewModel;

    public CheckoutPage(CheckoutViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.LoadCheckoutCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        viewModel.SaveDraftState();
        Debug.WriteLine("[Navigation] Leaving CheckoutPage (draft saved)");
    }
}
