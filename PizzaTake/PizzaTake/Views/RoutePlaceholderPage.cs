using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using PizzaTake.Models;
using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public class RoutePlaceholderPage : ContentPage
{
    public RoutePlaceholderPage(string title)
    {
        Title = title;
        Content = new Grid { Padding = 24, Children = { new Label { Text = $"{title} — екран у розробці", HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center } } };
    }
}
