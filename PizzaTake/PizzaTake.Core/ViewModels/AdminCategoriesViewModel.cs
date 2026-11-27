using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.ViewModels;

/// <summary>
/// Administrator-only ViewModel for managing catalog categories (add, rename, reorder, delete).
/// </summary>
public sealed class AdminCategoriesViewModel : BaseViewModel
{
    private readonly ICategoryRepository categoryRepository;
    private readonly IProductRepository productRepository;
    private readonly ISessionService sessionService;
    private readonly INavigationService navigation;
    private readonly IDialogService dialogService;
    private readonly IMainThreadDispatcher dispatcher;
    private readonly ILogger<AdminCategoriesViewModel> logger;

    private string newCategoryName = string.Empty;
    private ProductCategory? editingCategory;
    private string errorMessage = string.Empty;
    private string statusMessage = string.Empty;

    public AdminCategoriesViewModel(
        ICategoryRepository categoryRepository,
        IProductRepository productRepository,
        ISessionService sessionService,
        INavigationService navigation,
        IDialogService dialogService,
        IMainThreadDispatcher dispatcher,
        ILogger<AdminCategoriesViewModel>? logger = null)
    {
        this.categoryRepository = categoryRepository;
        this.productRepository = productRepository;
        this.sessionService = sessionService;
        this.navigation = navigation;
        this.dialogService = dialogService;
        this.dispatcher = dispatcher;
        this.logger = logger ?? NullLogger<AdminCategoriesViewModel>.Instance;
        Title = "Категорії меню";

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        SaveCategoryCommand = new AsyncRelayCommand(SaveCategoryAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(NewCategoryName));
        StartEditCommand = new RelayCommand<ProductCategory?>(StartEdit);
        CancelEditCommand = new RelayCommand(CancelEdit);
        MoveUpCommand = new AsyncRelayCommand<ProductCategory?>(c => MoveCategoryAsync(c, -1));
        MoveDownCommand = new AsyncRelayCommand<ProductCategory?>(c => MoveCategoryAsync(c, 1));
        DeleteCategoryCommand = new AsyncRelayCommand<ProductCategory?>(DeleteCategoryAsync);
        GoBackCommand = new AsyncRelayCommand(() => navigation.GoBackAsync());
    }

    public ObservableCollection<ProductCategory> Categories { get; } = [];
    public bool IsAdmin => sessionService.IsInRole(UserRole.Administrator);
    public bool IsEditing => editingCategory is not null;
    public string SaveButtonText => IsEditing ? "Оновити категорію" : "Додати категорію";

    public string NewCategoryName
    {
        get => newCategoryName;
        set
        {
            if (SetProperty(ref newCategoryName, value))
            {
                ErrorMessage = string.Empty;
                SaveCategoryCommand.NotifyCanExecuteChanged();
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

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (SetProperty(ref statusMessage, value))
                OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand SaveCategoryCommand { get; }
    public IRelayCommand<ProductCategory?> StartEditCommand { get; }
    public IRelayCommand CancelEditCommand { get; }
    public IAsyncRelayCommand<ProductCategory?> MoveUpCommand { get; }
    public IAsyncRelayCommand<ProductCategory?> MoveDownCommand { get; }
    public IAsyncRelayCommand<ProductCategory?> DeleteCategoryCommand { get; }
    public IAsyncRelayCommand GoBackCommand { get; }

    public async Task LoadAsync()
    {
        if (!sessionService.IsInRole(UserRole.Administrator))
        {
            logger.LogWarning("Access denied for role {Role} on {ViewModel}", sessionService.CurrentRole, nameof(AdminCategoriesViewModel));
            await dialogService.AlertAsync("Доступ заборонено", "Дія доступна лише адміністратору.");
            await navigation.GoBackAsync();
            return;
        }

        IsBusy = true;
        try
        {
            var items = await categoryRepository.GetAllAsync();
            await dispatcher.InvokeOnMainThreadAsync(() =>
            {
                Categories.Clear();
                foreach (var item in items)
                    Categories.Add(item);
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load admin categories");
            ErrorMessage = "Не вдалося завантажити категорії.";
        }
        finally
        {
            IsBusy = false;
            SaveCategoryCommand.NotifyCanExecuteChanged();
        }
    }

    private void StartEdit(ProductCategory? category)
    {
        if (category is null) return;
        editingCategory = category;
        NewCategoryName = category.Name;
        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(SaveButtonText));
    }

    private void CancelEdit()
    {
        editingCategory = null;
        NewCategoryName = string.Empty;
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(SaveButtonText));
    }

    private async Task SaveCategoryAsync()
    {
        if (!sessionService.IsInRole(UserRole.Administrator))
        {
            logger.LogWarning("Access denied for role {Role} on {Command}", sessionService.CurrentRole, nameof(SaveCategoryCommand));
            await dialogService.AlertAsync("Доступ заборонено", "Дія доступна лише адміністратору.");
            return;
        }

        var trimmed = NewCategoryName.Trim();
        if (trimmed.Length < 2)
        {
            ErrorMessage = "Назва категорії має містити щонайменше 2 символи.";
            return;
        }

        if (Categories.Any(c =>
                string.Equals(c.Name, trimmed, StringComparison.OrdinalIgnoreCase) &&
                (editingCategory is null || c.Id != editingCategory.Id)))
        {
            ErrorMessage = "Категорія з такою назвою вже існує.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            if (editingCategory is not null)
            {
                await categoryRepository.RenameAsync(editingCategory, trimmed);
                StatusMessage = $"Категорію перейменовано на «{trimmed}».";
                editingCategory = null;
                OnPropertyChanged(nameof(IsEditing));
                OnPropertyChanged(nameof(SaveButtonText));
            }
            else
            {
                var nextOrder = Categories.Count > 0 ? Categories.Max(c => c.SortOrder) + 1 : 0;
                await categoryRepository.SaveAsync(new ProductCategory
                {
                    Name = trimmed,
                    SortOrder = nextOrder
                });
                StatusMessage = $"Категорію «{trimmed}» додано.";
            }

            NewCategoryName = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save category {CategoryName}", trimmed);
            ErrorMessage = "Не вдалося зберегти категорію.";
        }
        finally
        {
            IsBusy = false;
            SaveCategoryCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task MoveCategoryAsync(ProductCategory? category, int delta)
    {
        if (category is null || IsBusy)
            return;

        if (!sessionService.IsInRole(UserRole.Administrator))
        {
            logger.LogWarning("Access denied for role {Role} on category reorder", sessionService.CurrentRole);
            await dialogService.AlertAsync("Доступ заборонено", "Дія доступна лише адміністратору.");
            return;
        }

        var index = Categories.IndexOf(category);
        var targetIndex = index + delta;
        if (index < 0 || targetIndex < 0 || targetIndex >= Categories.Count)
            return;

        var other = Categories[targetIndex];
        (category.SortOrder, other.SortOrder) = (other.SortOrder, category.SortOrder);
        if (category.SortOrder == other.SortOrder)
        {
            category.SortOrder = targetIndex;
            other.SortOrder = index;
        }

        await categoryRepository.SaveAsync(category);
        await categoryRepository.SaveAsync(other);
        await LoadAsync();
    }

    private async Task DeleteCategoryAsync(ProductCategory? category)
    {
        if (category is null || IsBusy)
            return;

        if (!sessionService.IsInRole(UserRole.Administrator))
        {
            logger.LogWarning("Access denied for role {Role} on {Command}", sessionService.CurrentRole, nameof(DeleteCategoryCommand));
            await dialogService.AlertAsync("Доступ заборонено", "Дія доступна лише адміністратору.");
            return;
        }

        var productsInCategory = await productRepository.GetByCategoryAsync(category.Name);
        if (productsInCategory.Count > 0)
        {
            ErrorMessage = $"Неможливо видалити «{category.Name}»: у категорії є {productsInCategory.Count} товар(ів). Спочатку перенесіть або видаліть їх.";
            return;
        }

        var confirmed = await dialogService.ConfirmAsync(
            "Видалення категорії",
            $"Видалити категорію «{category.Name}»?",
            "Видалити",
            "Скасувати");
        if (!confirmed)
            return;

        try
        {
            await categoryRepository.DeleteAsync(category);
            if (editingCategory?.Id == category.Id)
                CancelEdit();
            StatusMessage = $"Категорію «{category.Name}» видалено.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete category {CategoryName}", category.Name);
            ErrorMessage = "Не вдалося видалити категорію.";
        }
    }
}
