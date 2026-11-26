using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Models;
using PizzaTake.Repositories;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

/// <summary>
/// ViewModel for creating, editing, and deleting saved delivery addresses.
/// </summary>
public sealed class AddressEditViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IAddressRepository addressRepository;
    private readonly IUserProfileRepository userProfileRepository;
    private readonly ISyncService syncService;
    private readonly ILogger<AddressEditViewModel> logger;

    private UserAddress? editingAddress;
    private string label = "Дім";
    private string addressText = string.Empty;
    private string errorMessage = string.Empty;

    public AddressEditViewModel(
        IAddressRepository addressRepository,
        IUserProfileRepository userProfileRepository,
        ISyncService syncService,
        ILogger<AddressEditViewModel>? logger = null)
    {
        this.addressRepository = addressRepository;
        this.userProfileRepository = userProfileRepository;
        this.syncService = syncService;
        this.logger = logger ?? NullLogger<AddressEditViewModel>.Instance;
        Title = "Нова адреса";

        SaveCommand = new Command(async () => await SaveAsync(), CanSave);
        DeleteCommand = new Command(async () => await DeleteAsync(), () => IsEditMode && !IsBusy);
        GoBackCommand = new Command(async () => await Shell.Current.GoToAsync(".."));
    }

    public string Label
    {
        get => label;
        set
        {
            if (SetProperty(ref label, value))
            {
                ErrorMessage = string.Empty;
                ((Command)SaveCommand).ChangeCanExecute();
            }
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

    public bool IsEditMode => editingAddress is not null && editingAddress.Id > 0;

    public string ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (SetProperty(ref errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand GoBackCommand { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Address", out var value) && value is UserAddress address)
        {
            editingAddress = address;
            Label = string.IsNullOrWhiteSpace(address.Label) ? "Доставка" : address.Label;
            AddressText = address.Text;
            Title = "Редагування адреси";
        }
        else
        {
            editingAddress = null;
            Label = "Дім";
            AddressText = string.Empty;
            Title = "Нова адреса";
        }

        OnPropertyChanged(nameof(IsEditMode));
        ((Command)SaveCommand).ChangeCanExecute();
        ((Command)DeleteCommand).ChangeCanExecute();
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
        UpdateCommandStates();
        try
        {
            var isUpdate = IsEditMode;
            var profile = await userProfileRepository.GetCurrentAsync();
            var entity = editingAddress ?? new UserAddress { UserId = profile?.Id ?? 0 };
            entity.Label = Label.Trim();
            entity.Text = AddressText.Trim();
            entity.LastUsedAt = DateTime.UtcNow;

            await addressRepository.SaveAsync(entity);
            await syncService.EnqueueAsync("UserAddress", isUpdate ? "Update" : "Insert", new
            {
                entity.Id,
                entity.Label,
                entity.Text,
                UpdatedAt = DateTime.UtcNow
            });

            logger.LogInformation("Address {Operation} succeeded (Id={AddressId})", isUpdate ? "update" : "insert", entity.Id);
            await Shell.Current.GoToAsync("..", new Dictionary<string, object>
            {
                ["UpdatedAddress"] = entity
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save user address");
            ErrorMessage = "Не вдалося зберегти адресу. Спробуйте ще раз.";
        }
        finally
        {
            IsBusy = false;
            UpdateCommandStates();
        }
    }

    private async Task DeleteAsync()
    {
        if (editingAddress is null || editingAddress.Id <= 0 || IsBusy)
            return;

        IsBusy = true;
        UpdateCommandStates();
        try
        {
            var deletedId = editingAddress.Id;
            await addressRepository.DeleteAsync(editingAddress);
            await syncService.EnqueueAsync("UserAddress", "Delete", new
            {
                Id = deletedId,
                UpdatedAt = DateTime.UtcNow
            });

            logger.LogInformation("Address deleted (Id={AddressId})", deletedId);
            await Shell.Current.GoToAsync("..", new Dictionary<string, object>
            {
                ["DeletedAddressId"] = deletedId
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete user address {AddressId}", editingAddress.Id);
            ErrorMessage = "Не вдалося видалити адресу. Спробуйте ще раз.";
        }
        finally
        {
            IsBusy = false;
            UpdateCommandStates();
        }
    }

    private void UpdateCommandStates()
    {
        ((Command)SaveCommand).ChangeCanExecute();
        ((Command)DeleteCommand).ChangeCanExecute();
    }
}
