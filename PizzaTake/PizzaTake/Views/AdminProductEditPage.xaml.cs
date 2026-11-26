using PizzaTake.Core.ViewModels;

namespace PizzaTake.Views;

public partial class AdminProductEditPage : ContentPage, IQueryAttributable
{
    private readonly AdminProductEditViewModel viewModel;

    public AdminProductEditPage(AdminProductEditViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query) =>
        viewModel.ApplyQueryAttributes(query);

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
