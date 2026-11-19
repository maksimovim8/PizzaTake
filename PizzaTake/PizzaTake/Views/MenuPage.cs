using PizzaTake.ViewModels;
using PizzaTake.Services;

namespace PizzaTake.Views;

public partial class MenuPage : ContentPage
{
    private readonly MenuViewModel viewModel;
    private readonly IAppStateService appState;

    public MenuPage(MenuViewModel viewModel, IAppStateService appState)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.appState = appState;
        BindingContext = viewModel;
    }

    private async void OnCategoryClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string category })
            return;

        viewModel.SelectedCategory = category;
        var target = viewModel.Products.FirstOrDefault(product => product.Category == category)
            ?? (category == "Новинки"
                ? viewModel.Products.FirstOrDefault(product => product.IsNew)
                : category == "Standard"
                    ? viewModel.Products.LastOrDefault()
                    : viewModel.Products.Skip(1).FirstOrDefault());
        if (target is not null)
        {
            var index = viewModel.Products.IndexOf(target);
            if (index >= 0 && index < ProductList.Children.Count && ProductList.Children[index] is VisualElement card)
                await MenuScrollView.ScrollToAsync(0, ProductList.Y + card.Y, true);
        }

        foreach (var chip in CategoryChips.Children.OfType<Button>())
        {
            var selected = Equals(chip.CommandParameter, category);
            chip.BackgroundColor = selected ? Color.FromArgb("#FFF1D3") : Colors.White;
            chip.TextColor = selected ? Color.FromArgb("#F4511E") : Color.FromArgb("#777777");
        }
    }

    private async void OnPizzeriaTapped(object? sender, TappedEventArgs e)
    {
        appState.SelectedDeliveryType = PizzaTake.Models.DeliveryType.Pickup;
        appState.SaveState();
        await Shell.Current.GoToAsync(appState.SelectedCity is null ? "cities" : "pizzerias");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SelectedPizzeriaLabel.Text = appState.SelectedPizzeria?.StoreNumber ?? "Оберіть піцерію";
        viewModel.LoadCommand.Execute(null);
    }
}
