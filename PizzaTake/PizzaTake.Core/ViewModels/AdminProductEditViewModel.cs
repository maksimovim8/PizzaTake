using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.ViewModels;

/// <summary>
/// Administrator-only ViewModel for creating, editing, and deleting catalog pizzas.
/// Enforces Level 2 role authorization inside SaveProductCommand and DeleteProductCommand.
/// </summary>
public sealed class AdminProductEditViewModel : BaseViewModel, IQueryParameterReceiver
{
    private readonly IProductRepository productRepository;
    private readonly ISessionService sessionService;
    private readonly ISyncService syncService;
    private readonly INavigationService navigation;
    private readonly IDialogService dialogService;
    private readonly IMainThreadDispatcher dispatcher;
    private readonly IBugReportService? bugReportService;
    private readonly ILogger<AdminProductEditViewModel> logger;

    private Product? editingProduct;
    private string name = string.Empty;
    private string description = string.Empty;
    private string category = "Standard";
    private string sizeName = "Середня";
    private string weightGramsInput = "520";
    private string basePriceInput = "259";
    private string promoPriceInput = string.Empty;
    private string caloriesInput = "240";
    private bool isNew;
    private string errorMessage = string.Empty;
    private string? reportableErrorContext;

    public AdminProductEditViewModel(
        IProductRepository productRepository,
        ISessionService sessionService,
        ISyncService syncService,
        INavigationService navigation,
        IDialogService dialogService,
        IMainThreadDispatcher dispatcher,
        IBugReportService? bugReportService = null,
        ILogger<AdminProductEditViewModel>? logger = null)
    {
        this.productRepository = productRepository;
        this.sessionService = sessionService;
        this.syncService = syncService;
        this.navigation = navigation;
        this.dialogService = dialogService;
        this.dispatcher = dispatcher;
        this.bugReportService = bugReportService;
        this.logger = logger ?? NullLogger<AdminProductEditViewModel>.Instance;
        Title = "Нова піца";

        SaveProductCommand = new AsyncRelayCommand(SaveProductAsync, CanSaveProduct);
        DeleteProductCommand = new AsyncRelayCommand(DeleteProductAsync, () => IsEditMode && !IsBusy);
        ReportErrorCommand = new AsyncRelayCommand(ReportErrorAsync, () => CanReportError);
        GoBackCommand = new AsyncRelayCommand(() => navigation.GoBackAsync());
    }

    public string[] AvailableCategories { get; } = ["Новинки", "WOW піци", "Standard"];

    public bool IsAdmin => sessionService.IsInRole(UserRole.Administrator);
    public bool IsEditMode => editingProduct is not null && editingProduct.Id > 0;
    public bool CanDeleteProduct => IsAdmin && IsEditMode;

    public void StartSessionMonitoring()
    {
        sessionService.SessionChanged -= OnSessionChanged;
        sessionService.SessionChanged += OnSessionChanged;
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(CanDeleteProduct));
    }

    public void StopSessionMonitoring()
    {
        sessionService.SessionChanged -= OnSessionChanged;
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        dispatcher.BeginInvokeOnMainThread(() =>
        {
            OnPropertyChanged(nameof(IsAdmin));
            OnPropertyChanged(nameof(CanDeleteProduct));
        });
    }

    public string Name
    {
        get => name;
        set
        {
            if (SetProperty(ref name, value))
            {
                ErrorMessage = string.Empty;
                SaveProductCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string Description
    {
        get => description;
        set => SetProperty(ref description, value);
    }

    public string Category
    {
        get => category;
        set
        {
            if (SetProperty(ref category, value))
                SaveProductCommand.NotifyCanExecuteChanged();
        }
    }

    public string SizeName
    {
        get => sizeName;
        set => SetProperty(ref sizeName, value);
    }

    public string WeightGramsInput
    {
        get => weightGramsInput;
        set
        {
            if (SetProperty(ref weightGramsInput, value))
                ErrorMessage = string.Empty;
        }
    }

    public string BasePriceInput
    {
        get => basePriceInput;
        set
        {
            if (SetProperty(ref basePriceInput, value))
            {
                ErrorMessage = string.Empty;
                SaveProductCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string PromoPriceInput
    {
        get => promoPriceInput;
        set
        {
            if (SetProperty(ref promoPriceInput, value))
                ErrorMessage = string.Empty;
        }
    }

    public string CaloriesInput
    {
        get => caloriesInput;
        set => SetProperty(ref caloriesInput, value);
    }

    public bool IsNew
    {
        get => isNew;
        set => SetProperty(ref isNew, value);
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (SetProperty(ref errorMessage, value))
            {
                if (string.IsNullOrEmpty(value))
                    SetReportableErrorContext(null);
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool CanReportError => HasError && !string.IsNullOrEmpty(reportableErrorContext) && bugReportService is not null;

    public IAsyncRelayCommand SaveProductCommand { get; }
    public IAsyncRelayCommand DeleteProductCommand { get; }
    public IAsyncRelayCommand ReportErrorCommand { get; }
    public IAsyncRelayCommand GoBackCommand { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Product", out var productObj) && productObj is Product product)
        {
            editingProduct = product;
            Title = "Редагувати піцу";
            Name = product.Name;
            Description = product.Description;
            Category = string.IsNullOrWhiteSpace(product.Category) ? "Standard" : product.Category;
            SizeName = string.IsNullOrWhiteSpace(product.SizeName) ? "Середня" : product.SizeName;
            WeightGramsInput = product.WeightGrams.ToString(CultureInfo.InvariantCulture);
            BasePriceInput = product.BasePrice.ToString("0.##", CultureInfo.InvariantCulture);
            PromoPriceInput = product.PromoPrice?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
            CaloriesInput = product.Calories.ToString(CultureInfo.InvariantCulture);
            IsNew = product.IsNew;
        }
        else
        {
            editingProduct = null;
            Title = "Нова піца";
            Name = string.Empty;
            Description = string.Empty;
            Category = "Standard";
            SizeName = "Середня";
            WeightGramsInput = "520";
            BasePriceInput = "259";
            PromoPriceInput = string.Empty;
            CaloriesInput = "240";
            IsNew = false;
        }

        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(CanDeleteProduct));
        RefreshCommandStates();
    }

    private bool CanSaveProduct() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(Name) &&
        !string.IsNullOrWhiteSpace(Category) &&
        TryParseDecimal(BasePriceInput, out var price) &&
        price > 0m;

    private async Task SaveProductAsync()
    {
        if (!sessionService.IsInRole(UserRole.Administrator))
        {
            logger.LogWarning("Access denied for role {Role} on {Command}", sessionService.CurrentRole, nameof(SaveProductCommand));
            await dialogService.AlertAsync("Доступ заборонено", "Дія доступна лише адміністратору.");
            return;
        }

        if (IsBusy)
            return;

        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Вкажіть назву піци.";
            return;
        }

        if (!TryParseDecimal(BasePriceInput, out var basePrice) || basePrice <= 0m)
        {
            ErrorMessage = "Вкажіть коректну ціну (> 0).";
            return;
        }

        decimal? promoPrice = null;
        if (!string.IsNullOrWhiteSpace(PromoPriceInput))
        {
            if (!TryParseDecimal(PromoPriceInput, out var parsedPromo) || parsedPromo <= 0m)
            {
                ErrorMessage = "Акційна ціна має бути більшою за 0.";
                return;
            }
            if (parsedPromo >= basePrice)
            {
                ErrorMessage = "Акційна ціна має бути меншою за базову ціну.";
                return;
            }
            promoPrice = parsedPromo;
        }

        if (!int.TryParse(WeightGramsInput?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var weightGrams) || weightGrams <= 0)
        {
            ErrorMessage = "Вкажіть коректну вагу в грамах (> 0).";
            return;
        }

        _ = int.TryParse(CaloriesInput?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var calories);

        IsBusy = true;
        ErrorMessage = string.Empty;
        RefreshCommandStates();
        try
        {
            var isUpdate = IsEditMode;
            var product = editingProduct ?? new Product();
            product.Name = Name.Trim();
            product.Description = Description?.Trim() ?? string.Empty;
            product.Category = string.IsNullOrWhiteSpace(Category) ? "Standard" : Category.Trim();
            product.SizeName = string.IsNullOrWhiteSpace(SizeName) ? "Середня" : SizeName.Trim();
            product.WeightGrams = weightGrams;
            product.BasePrice = basePrice;
            product.PromoPrice = promoPrice;
            product.Calories = Math.Max(0, calories);
            product.IsNew = string.Equals(product.Category, "Новинки", StringComparison.OrdinalIgnoreCase);

            await productRepository.SaveAsync(product);
            await syncService.EnqueueAsync("Product", isUpdate ? "Update" : "Insert", new
            {
                product.Id,
                product.Name,
                product.Category,
                product.BasePrice,
                product.PromoPrice,
                product.IsNew,
                UpdatedAt = DateTime.UtcNow
            });

            logger.LogInformation("Administrator saved product {ProductName} (Id={ProductId}).", product.Name, product.Id);
            await navigation.GoBackAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save product {ProductName}.", Name);
            ErrorMessage = "Не вдалося зберегти товар. Спробуйте ще раз.";
            SetReportableErrorContext("AdminProductEdit: SaveProductAsync");
        }
        finally
        {
            IsBusy = false;
            RefreshCommandStates();
        }
    }

    private async Task DeleteProductAsync()
    {
        if (!sessionService.IsInRole(UserRole.Administrator))
        {
            logger.LogWarning("Access denied for role {Role} on {Command}", sessionService.CurrentRole, nameof(DeleteProductCommand));
            await dialogService.AlertAsync("Доступ заборонено", "Дія доступна лише адміністратору.");
            return;
        }

        if (!IsEditMode || editingProduct is null || IsBusy)
            return;

        var confirmed = await dialogService.ConfirmAsync(
            "Видалення піци",
            $"Видалити «{editingProduct.Name}» з каталогу?",
            "Видалити",
            "Скасувати");

        if (!confirmed)
            return;

        IsBusy = true;
        RefreshCommandStates();
        try
        {
            var deletedId = editingProduct.Id;
            var deletedName = editingProduct.Name;

            await productRepository.DeleteAsync(editingProduct);
            await syncService.EnqueueAsync("Product", "Delete", new
            {
                Id = deletedId,
                Name = deletedName,
                UpdatedAt = DateTime.UtcNow
            });

            logger.LogInformation("Administrator deleted product {ProductName} (Id={ProductId}).", deletedName, deletedId);
            await navigation.GoBackAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete product Id={ProductId}.", editingProduct.Id);
            ErrorMessage = "Не вдалося видалити товар. Спробуйте ще раз.";
            SetReportableErrorContext("AdminProductEdit: DeleteProductAsync");
        }
        finally
        {
            IsBusy = false;
            RefreshCommandStates();
        }
    }

    private void SetReportableErrorContext(string? context)
    {
        reportableErrorContext = context;
        OnPropertyChanged(nameof(CanReportError));
        ReportErrorCommand.NotifyCanExecuteChanged();
    }

    private async Task ReportErrorAsync()
    {
        if (bugReportService is null)
            return;

        await bugReportService.PromptAndSendAsync(reportableErrorContext ?? "Редагування піци");
    }

    private static bool TryParseDecimal(string? raw, out decimal result)
    {
        result = 0m;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var normalized = raw.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    private void RefreshCommandStates()
    {
        SaveProductCommand.NotifyCanExecuteChanged();
        DeleteProductCommand.NotifyCanExecuteChanged();
    }
}
