using System.Diagnostics;
using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class CitySelectionPage : ContentPage
{
    private readonly CitySelectionViewModel viewModel;

    public CitySelectionPage(CitySelectionViewModel viewModel)
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
        Debug.WriteLine("Leaving page: CitySelectionPage");
        base.OnDisappearing();
    }
}
