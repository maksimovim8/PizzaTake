namespace PizzaTake.Views;

public abstract class RoutePlaceholderPage : ContentPage
{
    protected RoutePlaceholderPage(string title)
    {
        Title = title;
        Content = new Grid { Padding = 24, Children = { new Label { Text = $"{title} — екран у розробці", HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center } } };
    }
}
public sealed class WelcomePage : RoutePlaceholderPage { public WelcomePage() : base("Вітаємо") { } }
public sealed class CitySelectionPage : RoutePlaceholderPage { public CitySelectionPage() : base("Вибір міста") { } }
public sealed class PizzeriaSelectionPage : RoutePlaceholderPage { public PizzeriaSelectionPage() : base("Вибір піцерії") { } }
public sealed class MenuPage : RoutePlaceholderPage { public MenuPage() : base("Меню") { } }
public sealed class PizzaDetailPage : RoutePlaceholderPage { public PizzaDetailPage() : base("Деталі піци") { } }
public sealed class FavoritesPage : RoutePlaceholderPage { public FavoritesPage() : base("Улюблене") { } }
public sealed class AddressEditPage : RoutePlaceholderPage { public AddressEditPage() : base("Редагування адреси") { } }
