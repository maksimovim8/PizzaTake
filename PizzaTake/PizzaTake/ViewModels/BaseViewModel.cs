using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PizzaTake.ViewModels;

public abstract class BaseViewModel : INotifyPropertyChanged
{
    private string title = string.Empty;
    private bool isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

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

    protected bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(backingStore, value))
            return false;

        backingStore = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
