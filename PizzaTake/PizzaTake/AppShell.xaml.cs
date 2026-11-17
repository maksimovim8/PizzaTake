namespace PizzaTake
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute("welcome", typeof(PizzaTake.Views.WelcomePage));
            Routing.RegisterRoute("cities", typeof(PizzaTake.Views.CitySelectionPage));
            Routing.RegisterRoute("pizzerias", typeof(PizzaTake.Views.PizzeriaSelectionPage));
            Routing.RegisterRoute("menu", typeof(PizzaTake.Views.MenuPage));
            Routing.RegisterRoute("pizzadetail", typeof(PizzaTake.Views.PizzaDetailPage));
            Routing.RegisterRoute("favorites", typeof(PizzaTake.Views.FavoritesPage));
            Routing.RegisterRoute("cart", typeof(PizzaTake.Views.CartPage));
            Routing.RegisterRoute("auth", typeof(PizzaTake.Views.AuthPage));
            Routing.RegisterRoute("checkout", typeof(PizzaTake.Views.CheckoutPage));
            Routing.RegisterRoute("history", typeof(PizzaTake.Views.OrderHistoryPage));
            Routing.RegisterRoute("orderdetail", typeof(PizzaTake.Views.OrderDetailPage));
            Routing.RegisterRoute("settings", typeof(PizzaTake.Views.SettingsPage));
            Routing.RegisterRoute("addressedit", typeof(PizzaTake.Views.AddressEditPage));
        }
    }
}
