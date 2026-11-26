using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Controls;
using PizzaTake.Models;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

public sealed class WelcomeViewModel : BaseViewModel
{
    public ICommand StartCommand { get; }

    public WelcomeViewModel(IAppStateService appState, ILogger<WelcomeViewModel>? logger = null)
    {
        var log = logger ?? NullLogger<WelcomeViewModel>.Instance;
        StartCommand = new Command(async () =>
        {
            log.LogInformation("Onboarding started from WelcomePage");
            appState.SelectedDeliveryType = DeliveryType.Delivery;
            appState.SaveState();
            await Shell.Current.GoToAsync("cities");
        });
    }
}
