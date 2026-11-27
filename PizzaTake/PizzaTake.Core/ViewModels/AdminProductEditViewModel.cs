using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.ViewModels;

/// <summary>
/// Administrator-only ViewModel for creating, editing, and deleting catalog products,
/// including full КБЖВ nutrition values, dynamic categories, and ingredient options.
/// Enforces Level 2 role authorization inside every mutating command.
/// </summary>
public sealed class AdminProductEditViewModel : FormFeedbackViewModel, IQueryParameterReceiver
{
    private static readonly string[] DefaultCategories = ["Новинки", "WOW піци", "Standard", "Закуски", "Соуси", "Напої"];

    private readonly IProductRepository productRepository;
    private readonly ISessionService sessionService;
    private readonly ISyncService syncService;
    private readonly INavigationService navigation;
    private readonly IDialogService dialogService;
    private readonly IMainThreadDispatcher dispatcher;
    private readonly ILogger<AdminProductEditViewModel> logger;
    private readonly ICategoryRepository? categoryRepository;
    private readonly SemaphoreSlim categoriesLoadLock = new(1, 1);

    private Product? editingProduct;
    private string name = string.Empty;
    private string description = string.Empty;
    private string category = "Standard";
    private string sizeName = "Середня";
    private string weightGramsInput = "520";
    private string basePriceInput = "259";
    private string promoPriceInput = string.Empty;
    private string caloriesInput = "240";
    private string proteinsInput = "12";
    private string fatsInput = "9";
    private string carbsInput = "26";
    private bool isNew;
    private string newOptionName = string.Empty;
    private string newOptionPriceInput = "25";
    private string selectedOptionKindName = "Додаток";

    public AdminProductEditViewModel(
        IProductRepository productRepository,
        ISessionService sessionService,
        ISyncService syncService,
        INavigationService navigation,
        IDialogService dialogService,
        IMainThreadDispatcher dispatcher,
        IBugReportService? bugReportService = null,
        ILogger<AdminProductEditViewModel>? logger = null,
        ICategoryRepository? categoryRepository = null)
        : base(bugReportService, "Редагування товару")
    {
        this.productRepository = productRepository;
        this.sessionService = sessionService;
        this.syncService = syncService;
        this.navigation = navigation;
        this.dialogService = dialogService;
        this.dispatcher = dispatcher;
        this.logger = logger ?? NullLogger<AdminProductEditViewModel>.Instance;
        this.categoryRepository = categoryRepository;
        Title = "Новий товар";

        ReplaceCollection(AvailableCategories, DefaultCategories);

        SaveProductCommand = new AsyncRelayCommand(SaveProductAsync, CanSaveProduct);
        DeleteProductCommand = new AsyncRelayCommand(DeleteProductAsync, () => IsEditMode && !IsBusy);
        AddOptionCommand = new AsyncRelayCommand(AddOptionAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(NewOptionName));
        DeleteOptionCommand = new AsyncRelayCommand<IngredientOption?>(DeleteOptionAsync);
        GoBackCommand = new AsyncRelayCommand(() => navigation.GoBackAsync());

        Options.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasOptions));
            OnPropertyChanged(nameof(HasNoOptions));
        };
    }

    public ObservableCollection<string> AvailableCategories { get; } = [];
    public ObservableCollection<IngredientOption> Options { get; } = [];
    public string[] OptionKindChoices { get; } = ["Додаток", "Прибрати", "Заміна"];

    public bool IsAdmin => sessionService.IsInRole(UserRole.Administrator);
    public bool IsEditMode => editingProduct is not null && editingProduct.Id > 0;
    public bool CanDeleteProduct => IsAdmin && IsEditMode;
    public bool HasOptions => Options.Count > 0;
    public bool HasNoOptions => Options.Count == 0;

    public string Name { get => name; set => SetInput(ref name, value, SaveProductCommand); }
    public string Description { get => description; set => SetProperty(ref description, value); }
    public string Category { get => category; set { if (SetProperty(ref category, value)) SaveProductCommand.NotifyCanExecuteChanged(); } }
    public string SizeName { get => sizeName; set => SetProperty(ref sizeName, value); }
    public string WeightGramsInput { get => weightGramsInput; set => SetInput(ref weightGramsInput, value); }
    public string BasePriceInput { get => basePriceInput; set => SetInput(ref basePriceInput, value, SaveProductCommand); }
    public string PromoPriceInput { get => promoPriceInput; set => SetInput(ref promoPriceInput, value); }
    public string CaloriesInput { get => caloriesInput; set => SetInput(ref caloriesInput, value); }
    public string ProteinsInput { get => proteinsInput; set => SetInput(ref proteinsInput, value); }
    public string FatsInput { get => fatsInput; set => SetInput(ref fatsInput, value); }
    public string CarbsInput { get => carbsInput; set => SetInput(ref carbsInput, value); }
    public bool IsNew { get => isNew; set => SetProperty(ref isNew, value); }
    public string NewOptionName { get => newOptionName; set => SetInput(ref newOptionName, value, AddOptionCommand); }
    public string NewOptionPriceInput { get => newOptionPriceInput; set => SetInput(ref newOptionPriceInput, value); }

    public string SelectedOptionKindName
    {
        get => selectedOptionKindName;
        set
        {
            if (SetProperty(ref selectedOptionKindName, value) && string.Equals(value, "Прибрати", StringComparison.Ordinal))
                NewOptionPriceInput = "0";
        }
    }

    public IAsyncRelayCommand SaveProductCommand { get; }
    public IAsyncRelayCommand DeleteProductCommand { get; }
    public IAsyncRelayCommand AddOptionCommand { get; }
    public IAsyncRelayCommand<IngredientOption?> DeleteOptionCommand { get; }
    public IAsyncRelayCommand GoBackCommand { get; }

    public void StartSessionMonitoring()
    {
        sessionService.SessionChanged -= OnSessionChanged;
        sessionService.SessionChanged += OnSessionChanged;
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(CanDeleteProduct));
        _ = LoadCategoriesAsync();
    }

    public void StopSessionMonitoring() => sessionService.SessionChanged -= OnSessionChanged;

    private void OnSessionChanged(object? sender, EventArgs e) =>
        dispatcher.BeginInvokeOnMainThread(() =>
        {
            OnPropertyChanged(nameof(IsAdmin));
            OnPropertyChanged(nameof(CanDeleteProduct));
        });

    public async Task LoadCategoriesAsync()
    {
        if (categoryRepository is null) return;
        await categoriesLoadLock.WaitAsync();
        try
        {
            var names = (await categoryRepository.GetAllAsync())
                .Select(c => c.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();
            if (names.Count == 0) names.AddRange(DefaultCategories);
            if (!string.IsNullOrWhiteSpace(Category) && !names.Contains(Category, StringComparer.OrdinalIgnoreCase))
                names.Add(Category);

            await dispatcher.InvokeOnMainThreadAsync(() =>
            {
                if (AvailableCategories.SequenceEqual(names, StringComparer.Ordinal))
                    return;

                var current = Category;
                ReplaceCollection(AvailableCategories, names);
                Category = AvailableCategories.FirstOrDefault(c => string.Equals(c, current, StringComparison.OrdinalIgnoreCase))
                    ?? AvailableCategories.FirstOrDefault() ?? "Standard";
            });
        }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to load dynamic categories in AdminProductEditViewModel"); }
        finally { categoriesLoadLock.Release(); }
    }

    public async Task LoadOptionsAsync()
    {
        if (editingProduct is null || editingProduct.Id <= 0) { Options.Clear(); return; }
        try
        {
            var loaded = await productRepository.GetOptionsByProductIdAsync(editingProduct.Id);
            await dispatcher.InvokeOnMainThreadAsync(() => ReplaceCollection(Options, loaded));
        }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to load ingredient options for product Id={ProductId}", editingProduct.Id); }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Product", out var productObj) && productObj is Product product)
        {
            editingProduct = product;
            Title = "Редагувати товар";
            Name = product.Name;
            Description = product.Description;
            Category = string.IsNullOrWhiteSpace(product.Category) ? "Standard" : product.Category;
            if (!AvailableCategories.Contains(Category, StringComparer.OrdinalIgnoreCase))
                AvailableCategories.Add(Category);
            SizeName = string.IsNullOrWhiteSpace(product.SizeName) ? "Середня" : product.SizeName;
            WeightGramsInput = product.WeightGrams.ToString(CultureInfo.InvariantCulture);
            BasePriceInput = product.BasePrice.ToString("0.##", CultureInfo.InvariantCulture);
            PromoPriceInput = product.PromoPrice?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
            CaloriesInput = product.Calories.ToString(CultureInfo.InvariantCulture);
            ProteinsInput = product.Proteins.ToString("0.##", CultureInfo.InvariantCulture);
            FatsInput = product.Fats.ToString("0.##", CultureInfo.InvariantCulture);
            CarbsInput = product.Carbs.ToString("0.##", CultureInfo.InvariantCulture);
            IsNew = product.IsNew;
            _ = LoadOptionsAsync();
        }
        else
        {
            editingProduct = null;
            Title = "Новий товар";
            Name = Description = PromoPriceInput = string.Empty;
            Category = AvailableCategories.Contains("Standard") ? "Standard" : AvailableCategories.FirstOrDefault() ?? "Standard";
            SizeName = "Середня";
            WeightGramsInput = "520";
            BasePriceInput = "259";
            CaloriesInput = "240";
            ProteinsInput = "12";
            FatsInput = "9";
            CarbsInput = "26";
            IsNew = false;
            Options.Clear();
        }

        NewOptionName = string.Empty;
        NewOptionPriceInput = "25";
        SelectedOptionKindName = "Додаток";

        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(CanDeleteProduct));
        RefreshCommandStates();
        _ = LoadCategoriesAsync();
    }

    private async Task AddOptionAsync()
    {
        if (!await RequireAdminAsync(sessionService, dialogService, logger, nameof(AddOptionCommand)) || IsBusy)
            return;

        var trimmedName = NewOptionName.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName)) { ErrorMessage = "Вкажіть назву добавки або модифікатора."; return; }

        var kind = SelectedOptionKindName switch
        {
            "Прибрати" => OptionKind.Exclusion,
            "Заміна" => OptionKind.Replacement,
            _ => OptionKind.AddOn
        };
        if (Options.Any(o => o.Kind == kind && string.Equals(o.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorMessage = "Така опція вже додана до цього товару.";
            return;
        }

        var price = 0m;
        if (kind != OptionKind.Exclusion && (!TryParseDecimal(NewOptionPriceInput, out price) || price < 0m))
        {
            ErrorMessage = "Ціна добавки має бути невід'ємним числом (≥ 0).";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        RefreshCommandStates();
        try
        {
            var option = new IngredientOption { ProductId = editingProduct?.Id ?? 0, Name = trimmedName, Price = price, Kind = kind };
            if (IsEditMode && editingProduct is not null)
                await productRepository.SaveOptionAsync(option);

            Options.Add(option);
            NewOptionName = string.Empty;
            NewOptionPriceInput = kind == OptionKind.Exclusion ? "0" : "25";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add ingredient option {OptionName}", trimmedName);
            ErrorMessage = "Не вдалося додати добавку. Спробуйте ще раз.";
        }
        finally { IsBusy = false; RefreshCommandStates(); }
    }

    private async Task DeleteOptionAsync(IngredientOption? option)
    {
        if (option is null || IsBusy || !await RequireAdminAsync(sessionService, dialogService, logger, nameof(DeleteOptionCommand)))
            return;

        try
        {
            if (option.Id > 0) await productRepository.DeleteOptionAsync(option);
            Options.Remove(option);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete ingredient option Id={OptionId}", option.Id);
            ErrorMessage = "Не вдалося видалити добавку.";
        }
    }

    private bool CanSaveProduct() =>
        !IsBusy && !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Category) &&
        TryParseDecimal(BasePriceInput, out var price) && price > 0m;

    private async Task SaveProductAsync()
    {
        if (!await RequireAdminAsync(sessionService, dialogService, logger, nameof(SaveProductCommand)) || IsBusy)
            return;

        if (string.IsNullOrWhiteSpace(Name)) { ErrorMessage = "Вкажіть назву товару."; return; }
        if (!TryParseDecimal(BasePriceInput, out var basePrice) || basePrice <= 0m) { ErrorMessage = "Вкажіть коректну ціну (> 0)."; return; }

        decimal? promoPrice = null;
        if (!string.IsNullOrWhiteSpace(PromoPriceInput))
        {
            if (!TryParseDecimal(PromoPriceInput, out var parsedPromo) || parsedPromo <= 0m) { ErrorMessage = "Акційна ціна має бути більшою за 0."; return; }
            if (parsedPromo >= basePrice) { ErrorMessage = "Акційна ціна має бути меншою за базову ціну."; return; }
            promoPrice = parsedPromo;
        }

        if (!int.TryParse(WeightGramsInput?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var weightGrams) || weightGrams <= 0)
        { ErrorMessage = "Вкажіть коректну вагу в грамах (> 0)."; return; }

        var calories = 0;
        if (!string.IsNullOrWhiteSpace(CaloriesInput) &&
            (!int.TryParse(CaloriesInput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out calories) || calories < 0))
        { ErrorMessage = "Вкажіть коректну калорійність (≥ 0)."; return; }

        if (!TryParseNonNegativeDecimal(ProteinsInput, out var proteins) ||
            !TryParseNonNegativeDecimal(FatsInput, out var fats) ||
            !TryParseNonNegativeDecimal(CarbsInput, out var carbs))
        { ErrorMessage = "Показники Б/Ж/В мають бути невід'ємними числами (≥ 0)."; return; }

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
            product.Calories = calories;
            product.Proteins = proteins;
            product.Fats = fats;
            product.Carbs = carbs;
            product.IsNew = string.Equals(product.Category, "Новинки", StringComparison.OrdinalIgnoreCase);

            await productRepository.SaveAsync(product);
            foreach (var option in Options.Where(o => o.Id == 0))
            {
                option.ProductId = product.Id;
                await productRepository.SaveOptionAsync(option);
            }

            await syncService.EnqueueAsync("Product", isUpdate ? "Update" : "Insert", new
            {
                product.Id, product.Name, product.Category, product.BasePrice, product.PromoPrice,
                product.Calories, product.Proteins, product.Fats, product.Carbs, UpdatedAt = DateTime.UtcNow
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
        finally { IsBusy = false; RefreshCommandStates(); }
    }

    private async Task DeleteProductAsync()
    {
        if (!await RequireAdminAsync(sessionService, dialogService, logger, nameof(DeleteProductCommand)) ||
            !IsEditMode || editingProduct is null || IsBusy)
            return;

        if (!await dialogService.ConfirmAsync("Видалення піци", $"Видалити «{editingProduct.Name}» з каталогу?", "Видалити", "Скасувати"))
            return;

        IsBusy = true;
        RefreshCommandStates();
        try
        {
            var (deletedId, deletedName) = (editingProduct.Id, editingProduct.Name);
            await productRepository.DeleteAsync(editingProduct);
            await syncService.EnqueueAsync("Product", "Delete", new { Id = deletedId, Name = deletedName, UpdatedAt = DateTime.UtcNow });
            logger.LogInformation("Administrator deleted product {ProductName} (Id={ProductId}).", deletedName, deletedId);
            await navigation.GoBackAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete product Id={ProductId}.", editingProduct.Id);
            ErrorMessage = "Не вдалося видалити товар. Спробуйте ще раз.";
            SetReportableErrorContext("AdminProductEdit: DeleteProductAsync");
        }
        finally { IsBusy = false; RefreshCommandStates(); }
    }

    private void RefreshCommandStates()
    {
        SaveProductCommand.NotifyCanExecuteChanged();
        DeleteProductCommand.NotifyCanExecuteChanged();
        AddOptionCommand.NotifyCanExecuteChanged();
    }
}
