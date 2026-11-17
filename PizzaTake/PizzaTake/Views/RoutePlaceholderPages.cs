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
public sealed class CartPage : RoutePlaceholderPage { public CartPage() : base("Кошик") { } }
public sealed class AuthPage : RoutePlaceholderPage { public AuthPage() : base("Авторизація") { } }
public sealed class CheckoutPage : RoutePlaceholderPage { public CheckoutPage() : base("Оформлення замовлення") { } }
public sealed class OrderHistoryPage : RoutePlaceholderPage { public OrderHistoryPage() : base("Історія замовлень") { } }
public sealed class OrderDetailPage : RoutePlaceholderPage { public OrderDetailPage() : base("Деталі замовлення") { } }
public sealed class SettingsPage : RoutePlaceholderPage { public SettingsPage() : base("Налаштування") { } }
public sealed class AddressEditPage : RoutePlaceholderPage { public AddressEditPage() : base("Редагування адреси") { } }
