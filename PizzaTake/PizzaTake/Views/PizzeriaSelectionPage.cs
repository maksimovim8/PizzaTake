using System.Diagnostics;
using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class PizzeriaSelectionPage : ContentPage
{
    private readonly PizzeriaSelectionViewModel viewModel;

    public PizzeriaSelectionPage(PizzeriaSelectionViewModel viewModel)
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
        Debug.WriteLine("Leaving page: PizzeriaSelectionPage");
        base.OnDisappearing();
    }
}
