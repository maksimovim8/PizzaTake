using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class MenuPage : ContentPage
{
    private readonly MenuViewModel viewModel;
    private readonly ILogger<MenuPage> logger;

    public MenuPage(MenuViewModel viewModel, ILogger<MenuPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.logger = logger ?? NullLogger<MenuPage>.Instance;
        BindingContext = viewModel;
    }

    private async void OnCategoryClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string category })
            return;

        viewModel.SelectedCategory = category;
        UpdateCategoryChipStyles(category);

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
                await MenuScrollView.ScrollToAsync(0, Math.Max(0, ProductList.Y + card.Y - 8), true);
        }
    }

    private void OnMenuScrolled(object? sender, ScrolledEventArgs e)
    {
        if (viewModel.Products.Count == 0 || ProductList.Children.Count == 0)
            return;

        string? visibleCategory = null;
        for (var i = 0; i < Math.Min(viewModel.Products.Count, ProductList.Children.Count); i++)
        {
            if (ProductList.Children[i] is not VisualElement card)
                continue;

            var cardTop = ProductList.Y + card.Y;
            if (cardTop <= e.ScrollY + 120)
                visibleCategory = viewModel.Products[i].Category;
        }

        visibleCategory ??= viewModel.Products[0].Category;
        if (!string.Equals(viewModel.SelectedCategory, visibleCategory, StringComparison.Ordinal))
        {
            viewModel.SelectedCategory = visibleCategory;
            UpdateCategoryChipStyles(visibleCategory);
        }
    }

    private void UpdateCategoryChipStyles(string? activeCategory)
    {
        var current = activeCategory ?? "Новинки";
        var appTheme = Application.Current?.UserAppTheme;
        var isDark = appTheme == AppTheme.Dark
            || (appTheme != AppTheme.Light && Application.Current?.RequestedTheme == AppTheme.Dark);

        foreach (var chip in CategoryChips.Children.OfType<Button>())
        {
            var selected = Equals(chip.CommandParameter, current);
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
        UpdateCategoryChipStyles(viewModel.SelectedCategory ?? "Новинки");
    }

    protected override void OnDisappearing()
    {
        logger.LogDebug("Leaving page {PageName}", nameof(MenuPage));
        base.OnDisappearing();
    }
}
