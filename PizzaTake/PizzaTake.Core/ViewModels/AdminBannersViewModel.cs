using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.ViewModels;

/// <summary>
/// Administrator-only ViewModel for creating, editing, toggling, and deleting promotional banners on MenuPage.
/// </summary>
public sealed class AdminBannersViewModel : FormFeedbackViewModel
{
    public sealed record BannerThemePreset(string Name, string BackgroundHex, string TitleColorHex, string BadgeColorHex, string BadgeTextColorHex);

    public static readonly IReadOnlyList<BannerThemePreset> ThemePresets =
    [
        new("Синій (Новинка)", "#2460A0", "#FFD166", "#F4511E", "#FFFFFF"),
        new("Бурштиновий (Хіт)", "#B45309", "#FFFFFF", "#1C1917", "#FFD166"),
        new("Зелений (Акція)", "#166534", "#FFFFFF", "#F7A600", "#FFFFFF"),
        new("Помаранчевий (Фірмовий)", "#C2410C", "#FFFFFF", "#1C1917", "#FFFFFF"),
        new("Темний (Преміум)", "#1F2937", "#FFD166", "#EB5E28", "#FFFFFF")
    ];

    private readonly IMenuBannerRepository bannerRepository;
    private readonly ISessionService sessionService;
    private readonly INavigationService navigation;
    private readonly IDialogService dialogService;
    private readonly IMainThreadDispatcher dispatcher;
    private readonly ILogger<AdminBannersViewModel> logger;
    private readonly ISyncService? syncService;

    private MenuBanner? editingBanner;
    private string eyebrow = string.Empty;
    private string bannerTitle = string.Empty;
    private string subtitle = string.Empty;
    private string badgeText = string.Empty;
    private string selectedThemeName = ThemePresets[0].Name;
    private bool isActive = true;

    public AdminBannersViewModel(
        IMenuBannerRepository bannerRepository,
        ISessionService sessionService,
        INavigationService navigation,
        IDialogService dialogService,
        IMainThreadDispatcher dispatcher,
        ILogger<AdminBannersViewModel>? logger = null,
        ISyncService? syncService = null)
    {
        this.bannerRepository = bannerRepository;
        this.sessionService = sessionService;
        this.navigation = navigation;
        this.dialogService = dialogService;
        this.dispatcher = dispatcher;
        this.logger = logger ?? NullLogger<AdminBannersViewModel>.Instance;
        this.syncService = syncService;
        Title = "Банери меню";

        AvailableThemeNames = ThemePresets.Select(p => p.Name).ToArray();

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        NewBannerCommand = new RelayCommand(ResetForm);
        EditBannerCommand = new RelayCommand<MenuBanner?>(SelectForEdit);
        SaveBannerCommand = new AsyncRelayCommand(SaveBannerAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(BannerTitle));
        ToggleActiveCommand = new AsyncRelayCommand<MenuBanner?>(ToggleActiveAsync);
        DeleteBannerCommand = new AsyncRelayCommand<MenuBanner?>(DeleteBannerAsync);
        GoBackCommand = new AsyncRelayCommand(() => navigation.GoBackAsync());
    }

    public ObservableCollection<MenuBanner> Banners { get; } = [];
    public string[] AvailableThemeNames { get; }
    public bool IsAdmin => sessionService.IsInRole(UserRole.Administrator);
    public bool IsEditing => editingBanner is not null;
    public string SaveButtonText => IsEditing ? "Оновити банер" : "Додати банер";

    public string Eyebrow { get => eyebrow; set => SetProperty(ref eyebrow, value); }
    public string BannerTitle { get => bannerTitle; set => SetInput(ref bannerTitle, value, SaveBannerCommand); }
    public string Subtitle { get => subtitle; set => SetProperty(ref subtitle, value); }
    public string BadgeText { get => badgeText; set => SetProperty(ref badgeText, value); }
    public string SelectedThemeName { get => selectedThemeName; set => SetProperty(ref selectedThemeName, value); }
    public bool IsActive { get => isActive; set => SetProperty(ref isActive, value); }

    public IAsyncRelayCommand LoadCommand { get; }
    public IRelayCommand NewBannerCommand { get; }
    public IRelayCommand<MenuBanner?> EditBannerCommand { get; }
    public IAsyncRelayCommand SaveBannerCommand { get; }
    public IAsyncRelayCommand<MenuBanner?> ToggleActiveCommand { get; }
    public IAsyncRelayCommand<MenuBanner?> DeleteBannerCommand { get; }
    public IAsyncRelayCommand GoBackCommand { get; }

    public async Task LoadAsync()
    {
        if (!await RequireAdminAsync(sessionService, dialogService, logger, nameof(AdminBannersViewModel)))
        {
            await navigation.GoBackAsync();
            return;
        }

        IsBusy = true;
        try
        {
            var list = await bannerRepository.GetAllAsync();
            await dispatcher.InvokeOnMainThreadAsync(() => ReplaceCollection(Banners, list));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load admin banners");
            ErrorMessage = "Не вдалося завантажити банери.";
        }
        finally { IsBusy = false; SaveBannerCommand.NotifyCanExecuteChanged(); }
    }

    private void SelectForEdit(MenuBanner? banner)
    {
        if (banner is null) return;
        editingBanner = banner;
        Eyebrow = banner.Eyebrow;
        BannerTitle = banner.Title;
        Subtitle = banner.Subtitle;
        BadgeText = banner.BadgeText;
        IsActive = banner.IsActive;
        SelectedThemeName = ThemePresets.FirstOrDefault(p =>
            string.Equals(p.BackgroundHex, banner.BackgroundHex, StringComparison.OrdinalIgnoreCase))?.Name ?? ThemePresets[0].Name;
        ErrorMessage = StatusMessage = string.Empty;
        NotifyEditState();
    }

    private void ResetForm()
    {
        editingBanner = null;
        Eyebrow = BannerTitle = Subtitle = BadgeText = ErrorMessage = string.Empty;
        SelectedThemeName = ThemePresets[0].Name;
        IsActive = true;
        NotifyEditState();
    }

    private void NotifyEditState()
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(SaveButtonText));
    }

    private async Task SaveBannerAsync()
    {
        if (!await RequireAdminAsync(sessionService, dialogService, logger, nameof(SaveBannerCommand)))
            return;

        if (string.IsNullOrWhiteSpace(BannerTitle)) { ErrorMessage = "Вкажіть заголовок банера."; return; }

        var preset = ThemePresets.FirstOrDefault(p => p.Name == SelectedThemeName) ?? ThemePresets[0];
        IsBusy = true;
        ErrorMessage = StatusMessage = string.Empty;
        try
        {
            var isUpdate = editingBanner is not null;
            var banner = editingBanner ?? new MenuBanner { SortOrder = Banners.Count > 0 ? Banners.Max(b => b.SortOrder) + 1 : 0 };

            banner.Eyebrow = string.IsNullOrWhiteSpace(Eyebrow) ? "АКЦІЯ" : Eyebrow.Trim();
            banner.Title = BannerTitle.Trim();
            banner.Subtitle = Subtitle?.Trim() ?? string.Empty;
            banner.BadgeText = string.IsNullOrWhiteSpace(BadgeText) ? "Детальніше" : BadgeText.Trim();
            banner.BackgroundHex = preset.BackgroundHex;
            banner.TitleColorHex = preset.TitleColorHex;
            banner.BadgeColorHex = preset.BadgeColorHex;
            banner.BadgeTextColorHex = preset.BadgeTextColorHex;
            banner.IsActive = IsActive;

            await bannerRepository.SaveAsync(banner);
            if (syncService is not null)
            {
                await syncService.EnqueueAsync("MenuBanner", isUpdate ? "Update" : "Insert", new
                {
                    banner.Id,
                    banner.Title,
                    banner.IsActive,
                    banner.SortOrder,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            ResetForm();
            StatusMessage = isUpdate ? "Банер оновлено." : "Новий банер додано.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save menu banner");
            ErrorMessage = "Не вдалося зберегти банер.";
        }
        finally { IsBusy = false; SaveBannerCommand.NotifyCanExecuteChanged(); }
    }

    private async Task ToggleActiveAsync(MenuBanner? banner)
    {
        if (banner is null || IsBusy || !await RequireAdminAsync(sessionService, dialogService, logger, nameof(ToggleActiveCommand)))
            return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            banner.IsActive = !banner.IsActive;
            await bannerRepository.SaveAsync(banner);
            if (syncService is not null)
            {
                await syncService.EnqueueAsync("MenuBanner", "Update", new
                {
                    banner.Id,
                    banner.Title,
                    banner.IsActive,
                    banner.SortOrder,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            StatusMessage = banner.IsActive ? $"Банер «{banner.Title}» увімкнено." : $"Банер «{banner.Title}» приховано.";
        }
        catch (Exception ex)
        {
            banner.IsActive = !banner.IsActive;
            logger.LogError(ex, "Failed to toggle banner Id={BannerId}", banner.Id);
            ErrorMessage = "Не вдалося оновити статус банера.";
            return;
        }
        finally { IsBusy = false; }

        await LoadAsync();
    }

    private async Task DeleteBannerAsync(MenuBanner? banner)
    {
        if (banner is null || IsBusy || !await RequireAdminAsync(sessionService, dialogService, logger, nameof(DeleteBannerCommand)))
            return;

        if (!await dialogService.ConfirmAsync("Видалення банера", $"Видалити банер «{banner.Title}»?", "Видалити", "Скасувати"))
            return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            await bannerRepository.DeleteAsync(banner);
            if (syncService is not null)
            {
                await syncService.EnqueueAsync("MenuBanner", "Delete", new
                {
                    banner.Id,
                    banner.Title,
                    banner.IsActive,
                    banner.SortOrder,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            if (editingBanner?.Id == banner.Id) ResetForm();
            StatusMessage = $"Банер «{banner.Title}» видалено.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete banner Id={BannerId}", banner.Id);
            ErrorMessage = "Не вдалося видалити банер.";
            return;
        }
        finally { IsBusy = false; }

        await LoadAsync();
    }
}
