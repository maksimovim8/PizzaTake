using System.Windows.Input;
using PizzaTake.Models;
using PizzaTake.Repositories;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

/// <summary>
/// ViewModel for creating, editing, and deleting saved delivery addresses with query-parameter return navigation
/// and draft persistence across app lifecycle transitions.
/// </summary>
public sealed class AddressEditViewModel : BaseViewModel, IQueryAttributable
{
    private const string DraftPrefix = "addressEdit.";
    private const string DraftLabelKey = "addressEdit.label";
    private const string DraftTextKey = "addressEdit.text";
    private const string DraftIdKey = "addressEdit.id";

    private readonly IAddressRepository addressRepository;
    private readonly IUserProfileRepository userProfileRepository;
    private readonly IAppStateService appState;

    private UserAddress? editingAddress;
    private string label = string.Empty;
    private string addressText = string.Empty;
    private string errorMessage = string.Empty;
    private bool isEditMode;
    private bool suppressDraftSync;
    private bool isDraftCleared;

    public AddressEditViewModel(
        IAddressRepository addressRepository,
        IUserProfileRepository userProfileRepository,
        IAppStateService appState)
    {
        this.addressRepository = addressRepository;
        this.userProfileRepository = userProfileRepository;
        this.appState = appState;
        Title = "Нова адреса";

        SaveCommand = new Command(async () => await SaveAsync(), CanSave);
        DeleteCommand = new Command(async () => await DeleteAsync(), CanDelete);
        GoBackCommand = new Command(async () => await GoBackAsync());
    }

    public string Label
    {
        get => label;
        set
        {
            if (SetProperty(ref label, value))
            {
                if (!suppressDraftSync)
                    appState.SetDraftValue(DraftLabelKey, value);
                ErrorMessage = string.Empty;
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
                if (!suppressDraftSync)
                    appState.SetDraftValue(DraftTextKey, value);
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
        isDraftCleared = false;
        suppressDraftSync = true;
        try
        {
            if (query.TryGetValue("Address", out var value) && value is UserAddress address)
            {
                editingAddress = address;
                IsEditMode = address.Id > 0;

                var draftId = appState.GetDraftValue(DraftIdKey);
                if (string.Equals(draftId, address.Id.ToString(), StringComparison.Ordinal))
                {
                    Label = appState.GetDraftValue(DraftLabelKey) ?? address.Label;
                    AddressText = appState.GetDraftValue(DraftTextKey) ?? address.Text;
                }
                else
                {
                    Label = address.Label;
                    AddressText = address.Text;
                }
            }
            else
            {
                editingAddress = null;
                IsEditMode = false;
                var draftId = appState.GetDraftValue(DraftIdKey);
                if (string.IsNullOrEmpty(draftId) || draftId == "0")
                {
                    Label = appState.GetDraftValue(DraftLabelKey) ?? string.Empty;
                    AddressText = appState.GetDraftValue(DraftTextKey) ?? string.Empty;
                }
                else
                {
                    Label = string.Empty;
                    AddressText = string.Empty;
                }
            }
        }
        finally
        {
            suppressDraftSync = false;
        }

        appState.SetDraftValue(DraftIdKey, (editingAddress?.Id ?? 0).ToString());
        appState.SetDraftValue(DraftLabelKey, Label);
        appState.SetDraftValue(DraftTextKey, AddressText);

        ErrorMessage = string.Empty;
        RefreshCommandStates();
    }

    public void SaveDraftState()
    {
        if (isDraftCleared)
            return;

        appState.SetDraftValue(DraftIdKey, (editingAddress?.Id ?? 0).ToString());
        appState.SetDraftValue(DraftLabelKey, Label);
        appState.SetDraftValue(DraftTextKey, AddressText);
        appState.SaveState();
    }

    private async Task GoBackAsync()
    {
        ClearDraft();
        await Shell.Current.GoToAsync("..");
    }

    private void ClearDraft()
    {
        isDraftCleared = true;
        appState.ClearDraftPrefix(DraftPrefix);
        appState.SaveState();
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
            ClearDraft();

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
            ClearDraft();

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
