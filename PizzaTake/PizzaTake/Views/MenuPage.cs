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
            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            chip.BackgroundColor = selected
                ? Color.FromArgb(isDark ? "#3A2D1E" : "#FFF1D3")
                : Color.FromArgb(isDark ? "#26201B" : "#FFFFFF");
            chip.TextColor = selected
                ? Color.FromArgb("#F4511E")
                : Color.FromArgb(isDark ? "#D6D3D1" : "#777777");
        }
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
