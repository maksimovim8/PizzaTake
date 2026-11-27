using CommunityToolkit.Mvvm.ComponentModel;

namespace PizzaTake.Core.ViewModels;

public abstract class BaseViewModel : ObservableObject
{
    private string title = string.Empty;
    private bool isBusy;

    public string Title
    {
        get => title;
        set => SetProperty(ref title, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        set => SetProperty(ref isBusy, value);
    }
}
