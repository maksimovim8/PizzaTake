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
public sealed class AdminCategoriesViewModel : FormFeedbackViewModel
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
    public string NewCategoryName { get => newCategoryName; set => SetInput(ref newCategoryName, value, SaveCategoryCommand); }

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
        if (!await RequireAdminAsync(sessionService, dialogService, logger, nameof(AdminCategoriesViewModel)))
        {
            await navigation.GoBackAsync();
            return;
        }

        IsBusy = true;
        try
        {
            var items = await categoryRepository.GetAllAsync();
            await dispatcher.InvokeOnMainThreadAsync(() => ReplaceCollection(Categories, items));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load admin categories");
            ErrorMessage = "Не вдалося завантажити категорії.";
        }
        finally { IsBusy = false; SaveCategoryCommand.NotifyCanExecuteChanged(); }
    }

    private void StartEdit(ProductCategory? category)
    {
        if (category is null) return;
        editingCategory = category;
        NewCategoryName = category.Name;
        StatusMessage = ErrorMessage = string.Empty;
        NotifyEditState();
    }

    private void CancelEdit()
    {
        editingCategory = null;
        NewCategoryName = ErrorMessage = string.Empty;
        NotifyEditState();
    }

    private void NotifyEditState()
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(SaveButtonText));
    }

    private async Task SaveCategoryAsync()
    {
        if (!await RequireAdminAsync(sessionService, dialogService, logger, nameof(SaveCategoryCommand)))
            return;

        var trimmed = NewCategoryName.Trim();
        if (trimmed.Length < 2) { ErrorMessage = "Назва категорії має містити щонайменше 2 символи."; return; }
        if (Categories.Any(c => string.Equals(c.Name, trimmed, StringComparison.OrdinalIgnoreCase) &&
                (editingCategory is null || c.Id != editingCategory.Id)))
        { ErrorMessage = "Категорія з такою назвою вже існує."; return; }

        IsBusy = true;
        ErrorMessage = StatusMessage = string.Empty;
        try
        {
            if (editingCategory is not null)
            {
                await categoryRepository.RenameAsync(editingCategory, trimmed);
                StatusMessage = $"Категорію перейменовано на «{trimmed}».";
                editingCategory = null;
                NotifyEditState();
            }
            else
            {
                var nextOrder = Categories.Count > 0 ? Categories.Max(c => c.SortOrder) + 1 : 0;
                await categoryRepository.SaveAsync(new ProductCategory { Name = trimmed, SortOrder = nextOrder });
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
        finally { IsBusy = false; SaveCategoryCommand.NotifyCanExecuteChanged(); }
    }

    private async Task MoveCategoryAsync(ProductCategory? category, int delta)
    {
        if (category is null || IsBusy || !await RequireAdminAsync(sessionService, dialogService, logger, "MoveCategory"))
            return;

        var index = Categories.IndexOf(category);
        var targetIndex = index + delta;
        if (index < 0 || targetIndex < 0 || targetIndex >= Categories.Count) return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var other = Categories[targetIndex];
            (category.SortOrder, other.SortOrder) = (other.SortOrder, category.SortOrder);
            if (category.SortOrder == other.SortOrder)
                (category.SortOrder, other.SortOrder) = (targetIndex, index);

            await categoryRepository.SaveAsync(category);
            await categoryRepository.SaveAsync(other);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reorder category {CategoryName}", category.Name);
            ErrorMessage = "Не вдалося змінити порядок категорій.";
            return;
        }
        finally { IsBusy = false; }

        await LoadAsync();
    }

    private async Task DeleteCategoryAsync(ProductCategory? category)
    {
        if (category is null || IsBusy || !await RequireAdminAsync(sessionService, dialogService, logger, nameof(DeleteCategoryCommand)))
            return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var productsInCategory = await productRepository.GetByCategoryAsync(category.Name);
            if (productsInCategory.Count > 0)
            {
                ErrorMessage = $"Неможливо видалити «{category.Name}»: у категорії є {productsInCategory.Count} товар(ів). Спочатку перенесіть або видаліть їх.";
                return;
            }

            if (!await dialogService.ConfirmAsync("Видалення категорії", $"Видалити категорію «{category.Name}»?", "Видалити", "Скасувати"))
                return;

            await categoryRepository.DeleteAsync(category);
            if (editingCategory?.Id == category.Id) CancelEdit();
            StatusMessage = $"Категорію «{category.Name}» видалено.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete category {CategoryName}", category.Name);
            ErrorMessage = "Не вдалося видалити категорію.";
            return;
        }
        finally { IsBusy = false; }

        await LoadAsync();
    }
}
