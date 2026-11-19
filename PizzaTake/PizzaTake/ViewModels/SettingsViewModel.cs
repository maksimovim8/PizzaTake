using System.Windows.Input;
using Microsoft.Maui.Controls;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

public sealed class SettingsViewModel : BaseViewModel
{
    private readonly IAppStateService appState;
    public bool IsDarkTheme => appState.IsDarkTheme;
    public string Phone => Preferences.Default.Get("profile.phone", "+380 XX XXX XX XX");
    public string City => appState.SelectedCity?.Name ?? "Київ";
    public ICommand ToggleThemeCommand { get; }

    public SettingsViewModel(IAppStateService appState)
    {
        this.appState = appState;
        ToggleThemeCommand = new Command<string>(mode =>
        {
            appState.ApplyTheme(mode == "dark");
            OnPropertyChanged(nameof(IsDarkTheme));
        });
    }
}
