using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.ViewModels;

public sealed class WelcomeViewModel : BaseViewModel
{
    public IAsyncRelayCommand StartCommand { get; }

    public WelcomeViewModel(
        IAppStateService appState,
        INavigationService navigation,
        ILogger<WelcomeViewModel>? logger = null)
    {
        var log = logger ?? NullLogger<WelcomeViewModel>.Instance;
        StartCommand = new AsyncRelayCommand(async () =>
        {
            log.LogInformation("Onboarding started from WelcomePage");
            appState.SelectedDeliveryType = DeliveryType.Delivery;
            appState.SaveState();
            await navigation.GoToAsync("cities");
        });
    }
}
