using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using PizzaTake.Models;
using PizzaTake.Repositories;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

public sealed class PizzeriaSelectionViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IPizzeriaRepository pizzerias;
    private readonly IAppStateService appState;
    private Pizzeria? selectedPizzeria;
    private string? returnTo;

    public ObservableCollection<Pizzeria> Pizzerias { get; } = [];
    public Pizzeria? SelectedPizzeria
    {
        get => selectedPizzeria;
        set
        {
            if (SetProperty(ref selectedPizzeria, value))
                ((Command)ConfirmPizzeriaCommand).ChangeCanExecute();
        }
    }

    public ICommand LoadCommand { get; }
    public ICommand SelectPizzeriaCommand { get; }
    public ICommand ConfirmPizzeriaCommand { get; }

    public PizzeriaSelectionViewModel(IPizzeriaRepository pizzerias, IAppStateService appState)
    {
        this.pizzerias = pizzerias;
        this.appState = appState;
        LoadCommand = new Command(async () => await LoadAsync());
        SelectPizzeriaCommand = new Command<Pizzeria>(pizzeria => SelectedPizzeria = pizzeria);
        ConfirmPizzeriaCommand = new Command(async () => await ConfirmAsync(), () => SelectedPizzeria is not null);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        returnTo = query.TryGetValue("returnTo", out var value) && value?.ToString() is "settings" or "menu" or "cart" or "checkout"
            ? value.ToString()
            : null;
    }

    private async Task LoadAsync()
    {
        if (appState.SelectedCity is null) return;
        IsBusy = true;
        try
        {
            Pizzerias.Clear();
            foreach (var pizzeria in await pizzerias.GetByCityIdAsync(appState.SelectedCity.Id))
                Pizzerias.Add(pizzeria);
        }
        finally { IsBusy = false; }
    }

    private async Task ConfirmAsync()
    {
        if (SelectedPizzeria is null) return;
        appState.SelectedPizzeria = SelectedPizzeria;
        appState.SaveState();
        if (returnTo == "checkout")
        {
            var previousPage = Shell.Current.Navigation.NavigationStack.Reverse().Skip(1).FirstOrDefault();
            await Shell.Current.GoToAsync(previousPage is Views.CitySelectionPage ? "../.." : "..");
            return;
        }

        await Shell.Current.GoToAsync(returnTo switch
        {
            "settings" => "///settings",
            "cart" => "///cart",
            _ => "///menu"
        });
    }
}
