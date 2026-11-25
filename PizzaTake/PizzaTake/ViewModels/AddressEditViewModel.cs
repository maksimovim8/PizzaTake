using System.Windows.Input;
using PizzaTake.Models;
using PizzaTake.Repositories;

namespace PizzaTake.ViewModels;

/// <summary>
/// ViewModel for creating, editing, and deleting saved delivery addresses with query-parameter return navigation.
/// </summary>
public sealed class AddressEditViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IAddressRepository addressRepository;
    private readonly IUserProfileRepository userProfileRepository;

    private UserAddress? editingAddress;
    private string label = string.Empty;
    private string addressText = string.Empty;
    private string errorMessage = string.Empty;
    private bool isEditMode;

    public AddressEditViewModel(
        IAddressRepository addressRepository,
        IUserProfileRepository userProfileRepository)
    {
        this.addressRepository = addressRepository;
        this.userProfileRepository = userProfileRepository;
        Title = "Нова адреса";

        SaveCommand = new Command(async () => await SaveAsync(), CanSave);
        DeleteCommand = new Command(async () => await DeleteAsync(), CanDelete);
        GoBackCommand = new Command(async () => await Shell.Current.GoToAsync(".."));
    }

    public string Label
    {
        get => label;
        set
        {
            if (SetProperty(ref label, value))
                ErrorMessage = string.Empty;
        }
    }

    public string AddressText
    {
        get => addressText;
        set
        {
            if (SetProperty(ref addressText, value))
            {
                ErrorMessage = string.Empty;
                ((Command)SaveCommand).ChangeCanExecute();
            }
        }
    }

    public bool IsEditMode
    {
        get => isEditMode;
        private set
        {
            if (SetProperty(ref isEditMode, value))
            {
                Title = value ? "Редагування адреси" : "Нова адреса";
                ((Command)DeleteCommand).ChangeCanExecute();
            }
        }
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (SetProperty(ref errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand GoBackCommand { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Address", out var value) && value is UserAddress address)
        {
            editingAddress = address;
            Label = address.Label;
            AddressText = address.Text;
            IsEditMode = address.Id > 0;
        }
        else
        {
            editingAddress = null;
            Label = string.Empty;
            AddressText = string.Empty;
            IsEditMode = false;
        }

        ErrorMessage = string.Empty;
        ((Command)SaveCommand).ChangeCanExecute();
        ((Command)DeleteCommand).ChangeCanExecute();
    }

    private bool CanSave() => !IsBusy && !string.IsNullOrWhiteSpace(AddressText);

    private bool CanDelete() => !IsBusy && IsEditMode && editingAddress is not null;

    private async Task SaveAsync()
    {
        if (!CanSave())
            return;

        var trimmedText = AddressText.Trim();
        if (trimmedText.Length < 4)
        {
            ErrorMessage = "Введіть повну адресу доставки (щонайменше 4 символи).";
            return;
        }

        IsBusy = true;
        RefreshCommandStates();
        try
        {
            var profile = await userProfileRepository.GetCurrentAsync();
            var address = editingAddress is not null
                ? new UserAddress
                {
                    Id = editingAddress.Id,
                    UserId = editingAddress.UserId != 0 ? editingAddress.UserId : profile?.Id ?? 0,
                    Label = string.IsNullOrWhiteSpace(Label) ? "Доставка" : Label.Trim(),
                    Text = trimmedText,
                    LastUsedAt = DateTime.UtcNow
                }
                : new UserAddress
                {
                    UserId = profile?.Id ?? 0,
                    Label = string.IsNullOrWhiteSpace(Label) ? "Доставка" : Label.Trim(),
                    Text = trimmedText,
                    LastUsedAt = DateTime.UtcNow
                };

            await addressRepository.SaveAsync(address);

            await Shell.Current.GoToAsync("..", new Dictionary<string, object>
            {
                ["UpdatedAddress"] = address
            });
        }
        finally
        {
            IsBusy = false;
            RefreshCommandStates();
        }
    }

    private async Task DeleteAsync()
    {
        if (!CanDelete() || editingAddress is null)
            return;

        IsBusy = true;
        RefreshCommandStates();
        try
        {
            var deletedId = editingAddress.Id;
            await addressRepository.DeleteAsync(editingAddress);

            await Shell.Current.GoToAsync("..", new Dictionary<string, object>
            {
                ["DeletedAddressId"] = deletedId
            });
        }
        finally
        {
            IsBusy = false;
            RefreshCommandStates();
        }
    }

    private void RefreshCommandStates()
    {
        ((Command)SaveCommand).ChangeCanExecute();
        ((Command)DeleteCommand).ChangeCanExecute();
    }
}
