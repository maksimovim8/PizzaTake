using System.Windows.Input;
using PizzaTake.Models;
using PizzaTake.Repositories;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

/// <summary>
/// ViewModel for creating, editing, and deleting a saved user delivery address.
/// </summary>
public sealed class AddressEditViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IAddressRepository addressRepository;
    private readonly ISyncService syncService;

    private UserAddress editingAddress = new();
    private string label = string.Empty;
    private string addressText = string.Empty;

    public AddressEditViewModel(IAddressRepository addressRepository, ISyncService syncService)
    {
        this.addressRepository = addressRepository;
        this.syncService = syncService;
        Title = "Адреса доставки";

        SaveCommand = new Command(async () => await SaveAsync(), CanSave);
        DeleteCommand = new Command(async () => await DeleteAsync(), () => IsExistingAddress);
        GoBackCommand = new Command(async () => await Shell.Current.GoToAsync(".."));
    }

    public UserAddress EditingAddress
    {
        get => editingAddress;
        private set
        {
            if (SetProperty(ref editingAddress, value))
            {
                OnPropertyChanged(nameof(IsExistingAddress));
                ((Command)DeleteCommand).ChangeCanExecute();
            }
        }
    }

    public string Label
    {
        get => label;
        set
        {
            if (SetProperty(ref label, value))
                ((Command)SaveCommand).ChangeCanExecute();
        }
    }

    public string AddressText
    {
        get => addressText;
        set
        {
            if (SetProperty(ref addressText, value))
                ((Command)SaveCommand).ChangeCanExecute();
        }
    }

    public bool IsExistingAddress => EditingAddress.Id != 0;

    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand GoBackCommand { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Address", out var addressObj) && addressObj is UserAddress existing)
        {
            EditingAddress = new UserAddress
            {
                Id = existing.Id,
                UserId = existing.UserId,
                Label = existing.Label,
                Text = existing.Text,
                LastUsedAt = existing.LastUsedAt
            };
            Label = existing.Label;
            AddressText = existing.Text;
            Title = "Редагувати адресу";
        }
        else
        {
            EditingAddress = new UserAddress();
            Label = string.Empty;
            AddressText = string.Empty;
            Title = "Нова адреса";
        }
    }

    private bool CanSave() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(Label) &&
        !string.IsNullOrWhiteSpace(AddressText);

    private async Task SaveAsync()
    {
        if (!CanSave())
            return;

        IsBusy = true;
        try
        {
            var isNew = EditingAddress.Id == 0;
            EditingAddress.Label = Label.Trim();
            EditingAddress.Text = AddressText.Trim();
            EditingAddress.LastUsedAt = DateTime.UtcNow;

            await addressRepository.SaveAsync(EditingAddress);
            await syncService.EnqueueAsync("UserAddress", isNew ? "Insert" : "Update", new
            {
                EditingAddress.Id,
                EditingAddress.Label,
                EditingAddress.Text,
                UpdatedAt = EditingAddress.LastUsedAt
            });

            await Shell.Current.GoToAsync("..", new Dictionary<string, object>
            {
                ["UpdatedAddress"] = EditingAddress
            });
        }
        finally
        {
            IsBusy = false;
            ((Command)SaveCommand).ChangeCanExecute();
        }
    }

    private async Task DeleteAsync()
    {
        if (!IsExistingAddress || IsBusy)
            return;

        IsBusy = true;
        try
        {
            var deletedId = EditingAddress.Id;
            await addressRepository.DeleteAsync(EditingAddress);
            await syncService.EnqueueAsync("UserAddress", "Delete", new
            {
                Id = deletedId,
                UpdatedAt = DateTime.UtcNow
            });

            await Shell.Current.GoToAsync("..", new Dictionary<string, object>
            {
                ["DeletedAddressId"] = deletedId
            });
        }
        finally
        {
            IsBusy = false;
        }
    }
}
