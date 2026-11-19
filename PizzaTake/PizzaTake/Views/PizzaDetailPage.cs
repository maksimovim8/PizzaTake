using System.Diagnostics;
using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class PizzaDetailPage : ContentPage
{
    private readonly PizzaDetailViewModel viewModel;

    public PizzaDetailPage(PizzaDetailViewModel viewModel)
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
        Debug.WriteLine("Leaving page: PizzaDetailPage");
        base.OnDisappearing();
    }

    private async void OnKbjuClicked(object? sender, EventArgs e)
    {
        await DisplayAlertAsync("Харчова цінність", viewModel.KbjuSummary, "Зрозуміло");
    }
}
