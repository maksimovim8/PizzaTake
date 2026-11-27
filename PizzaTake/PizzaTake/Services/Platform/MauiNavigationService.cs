using PizzaTake.Core.Services;

namespace PizzaTake.Services.Platform;

public sealed class MauiNavigationService : INavigationService
{
    public Task GoToAsync(string route, IDictionary<string, object>? parameters = null) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Shell.Current is null)
                return;

            if (parameters is not null)
                await Shell.Current.GoToAsync(route, new Dictionary<string, object>(parameters));
            else
                await Shell.Current.GoToAsync(route);
        });

    public Task GoBackAsync(IDictionary<string, object>? parameters = null) =>
        GoToAsync("..", parameters);

    public Task PopToRootAsync(bool animated = false) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Shell.Current?.Navigation is not null)
                await Shell.Current.Navigation.PopToRootAsync(animated);
        });
}
