using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using PizzaTake.Models;
using PizzaTake.Repositories;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

public sealed class WelcomeViewModel(IAppStateService appState) : BaseViewModel
{
    public ICommand StartCommand { get; } = new Command(async () =>
    {
        appState.SelectedDeliveryType = DeliveryType.Delivery;
        appState.SaveState();
        await Shell.Current.GoToAsync("cities");
    });
}
