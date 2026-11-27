using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.ViewModels;

public sealed class PizzeriaSelectionViewModel : BaseViewModel, IQueryParameterReceiver
{
    private readonly IPizzeriaRepository pizzerias;
    private readonly IAppStateService appState;
    private readonly INavigationService navigation;
    private readonly ILogger<PizzeriaSelectionViewModel> logger;
    private Pizzeria? selectedPizzeria;
    private string? returnTo;

    public ObservableCollection<Pizzeria> Pizzerias { get; } = [];

    public Pizzeria? SelectedPizzeria
    {
        get => selectedPizzeria;
        set
        {
            if (SetProperty(ref selectedPizzeria, value))
            {
                foreach (var pizzeria in Pizzerias)
                    pizzeria.IsSelected = selectedPizzeria is not null && pizzeria.Id == selectedPizzeria.Id;
                ConfirmPizzeriaCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IAsyncRelayCommand LoadCommand { get; }
    public IRelayCommand<Pizzeria> SelectPizzeriaCommand { get; }
    public IAsyncRelayCommand ConfirmPizzeriaCommand { get; }
    public IAsyncRelayCommand OpenMapCommand { get; }
    public IAsyncRelayCommand GoBackCommand { get; }

    public PizzeriaSelectionViewModel(
        IPizzeriaRepository pizzerias,
        IAppStateService appState,
        INavigationService navigation,
        ILogger<PizzeriaSelectionViewModel>? logger = null)
    {
        this.pizzerias = pizzerias;
        this.appState = appState;
        this.navigation = navigation;
        this.logger = logger ?? NullLogger<PizzeriaSelectionViewModel>.Instance;

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        SelectPizzeriaCommand = new RelayCommand<Pizzeria>(pizzeria => SelectedPizzeria = pizzeria);
        ConfirmPizzeriaCommand = new AsyncRelayCommand(ConfirmAsync, () => SelectedPizzeria is not null);
        OpenMapCommand = new AsyncRelayCommand(() => navigation.GoToAsync("pizzeriasmap"));
        GoBackCommand = new AsyncRelayCommand(() => navigation.GoBackAsync());
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        returnTo = query.TryGetValue("returnTo", out var value) && value?.ToString() is "settings" or "menu" or "cart"
            ? value.ToString()
            : null;
    }

    public async Task LoadAsync()
    {
        if (appState.SelectedCity is null) return;
        IsBusy = true;
        try
        {
            Pizzerias.Clear();
            var list = await pizzerias.GetByCityIdAsync(appState.SelectedCity.Id);
            foreach (var pizzeria in list)
                Pizzerias.Add(pizzeria);

            var currentId = appState.SelectedPizzeria?.Id;
            var preselected = currentId.HasValue
                ? Pizzerias.FirstOrDefault(p => p.Id == currentId.Value)
                : null;
            SelectedPizzeria = preselected;
            if (preselected is not null)
                preselected.IsSelected = true;

            logger.LogInformation("Loaded {PizzeriaCount} pizzeria(s) for city {CityName}", Pizzerias.Count, appState.SelectedCity.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load pizzerias for city {CityId}", appState.SelectedCity.Id);
        }
        finally { IsBusy = false; }
    }

    private async Task ConfirmAsync()
    {
        if (SelectedPizzeria is null) return;
        appState.SelectedPizzeria = SelectedPizzeria;
        appState.SaveState();
        logger.LogInformation("Confirmed pizzeria {StoreNumber} ({Address})", SelectedPizzeria.StoreNumber, SelectedPizzeria.Address);
        await navigation.GoToAsync(returnTo switch
        {
            "settings" => "///settings",
            "cart" => "///cart",
            _ => "///menu"
        });
    }
}
