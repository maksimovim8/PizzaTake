using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.ViewModels;

/// <summary>
/// ViewModel for order checkout, saved address selection, draft persistence, and offline queue submission.
/// </summary>
public sealed class CheckoutViewModel : BaseViewModel
{
    private const string DraftPrefix = "checkout.";
    private const string DraftAddressKey = "checkout.address";
    private const string DraftDeliveryTypeKey = "checkout.deliveryType";
    private const string DraftPaymentMethodKey = "checkout.paymentMethod";
    private const string LegacyDraftPreferenceKey = "checkout_draft_state";

    private readonly ICartRepository cartRepository;
    private readonly IOrderRepository orderRepository;
    private readonly IAddressRepository addressRepository;
    private readonly IUserProfileRepository userProfileRepository;
    private readonly ISyncService syncService;
    private readonly IAppStateService appState;
    private readonly INavigationService navigation;
    private readonly ISettingsService settings;
    private readonly IConnectivityService connectivityService;
    private readonly IMainThreadDispatcher dispatcher;
    private readonly ILogger<CheckoutViewModel> logger;
    private readonly ICrashReportingService? crashReporting;
    private readonly IBugReportService? bugReportService;

    private string deliveryAddress = string.Empty;
    private DeliveryType deliveryType = DeliveryType.Delivery;
    private PaymentMethod paymentMethod = PaymentMethod.CardOnline;
    private decimal totalAmount;
    private int itemsCount;
    private bool isOffline;
    private string errorMessage = string.Empty;
    private string? reportableErrorContext;
    private bool isOrderSubmitted;

    public CheckoutViewModel(
        ICartRepository cartRepository,
        IOrderRepository orderRepository,
        IAddressRepository addressRepository,
        IUserProfileRepository userProfileRepository,
        ISyncService syncService,
        IAppStateService appState,
        INavigationService navigation,
        ISettingsService settings,
        IConnectivityService connectivityService,
        IMainThreadDispatcher dispatcher,
        ILogger<CheckoutViewModel>? logger = null,
        ICrashReportingService? crashReporting = null,
        IBugReportService? bugReportService = null)
    {
        this.cartRepository = cartRepository;
        this.orderRepository = orderRepository;
        this.addressRepository = addressRepository;
        this.userProfileRepository = userProfileRepository;
        this.syncService = syncService;
        this.appState = appState;
        this.navigation = navigation;
        this.settings = settings;
        this.connectivityService = connectivityService;
        this.dispatcher = dispatcher;
        this.logger = logger ?? NullLogger<CheckoutViewModel>.Instance;
        this.crashReporting = crashReporting;
        this.bugReportService = bugReportService;
        Title = "Оплата замовлення";

        LoadCheckoutCommand = new AsyncRelayCommand(LoadCheckoutAsync);
        SelectAddressCommand = new RelayCommand<UserAddress?>(SelectAddress);
        SelectDeliveryTypeCommand = new RelayCommand<string?>(SelectDeliveryType);
        SelectPaymentMethodCommand = new RelayCommand<string?>(SelectPaymentMethod);
        ConfirmPaymentCommand = new AsyncRelayCommand(ConfirmPaymentAsync, CanConfirmPayment);
        ReportErrorCommand = new AsyncRelayCommand(ReportErrorAsync, () => CanReportError);
        GoBackCommand = new AsyncRelayCommand(() => navigation.GoBackAsync());
    }

    public ObservableCollection<UserAddress> SavedAddresses { get; } = new();

    public string DeliveryAddress
    {
        get => deliveryAddress;
        set
        {
            if (SetProperty(ref deliveryAddress, value))
            {
                appState.SetDraftValue(DraftAddressKey, value);
                ErrorMessage = string.Empty;
                ConfirmPaymentCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public DeliveryType DeliveryType
    {
        get => deliveryType;
        set
        {
            if (SetProperty(ref deliveryType, value))
            {
                appState.SetDraftValue(DraftDeliveryTypeKey, value.ToString());
                OnPropertyChanged(nameof(IsDelivery));
                OnPropertyChanged(nameof(IsPickup));
                ConfirmPaymentCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsDelivery => DeliveryType == DeliveryType.Delivery;
    public bool IsPickup => DeliveryType == DeliveryType.Pickup;

    public PaymentMethod PaymentMethod
    {
        get => paymentMethod;
        set
        {
            if (SetProperty(ref paymentMethod, value))
            {
                appState.SetDraftValue(DraftPaymentMethodKey, value.ToString());
                OnPropertyChanged(nameof(IsCardOnline));
                OnPropertyChanged(nameof(IsCardOnDelivery));
                OnPropertyChanged(nameof(IsCash));
            }
        }
    }

    public bool IsCardOnline => PaymentMethod == PaymentMethod.CardOnline;
    public bool IsCardOnDelivery => PaymentMethod == PaymentMethod.CardOnDelivery;
    public bool IsCash => PaymentMethod == PaymentMethod.Cash;

    public decimal TotalAmount
    {
        get => totalAmount;
        private set => SetProperty(ref totalAmount, value);
    }

    public int ItemsCount
    {
        get => itemsCount;
        private set
        {
            if (SetProperty(ref itemsCount, value))
                ConfirmPaymentCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsOffline
    {
        get => isOffline;
        private set => SetProperty(ref isOffline, value);
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

    public IAsyncRelayCommand LoadCheckoutCommand { get; }
    public IRelayCommand<UserAddress?> SelectAddressCommand { get; }
    public IRelayCommand<string?> SelectDeliveryTypeCommand { get; }
    public IRelayCommand<string?> SelectPaymentMethodCommand { get; }
    public IAsyncRelayCommand ConfirmPaymentCommand { get; }
    public IAsyncRelayCommand ReportErrorCommand { get; }
    public IAsyncRelayCommand GoBackCommand { get; }

    public async Task LoadCheckoutAsync()
    {
        if (IsBusy)
            return;

        isOrderSubmitted = false;
        IsBusy = true;
        try
        {
            IsOffline = !connectivityService.IsConnected;

            var cartItems = await cartRepository.GetAllAsync();
            ItemsCount = cartItems.Sum(i => i.Quantity);
            TotalAmount = cartItems.Sum(i => i.CalculateSubtotal());

            var addresses = await addressRepository.GetAllAsync();
            SavedAddresses.Clear();
            foreach (var address in addresses)
                SavedAddresses.Add(address);

            RestoreDraftState();

            if (string.IsNullOrWhiteSpace(DeliveryAddress) && SavedAddresses.Count > 0)
                DeliveryAddress = SavedAddresses[0].Text;

            logger.LogInformation("Checkout loaded: {ItemsCount} item(s), Total={TotalAmount}, IsOffline={IsOffline}", ItemsCount, TotalAmount, IsOffline);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load checkout state");
            ErrorMessage = "Не вдалося завантажити дані оформлення.";
            SetReportableErrorContext("Checkout: LoadCheckoutAsync");
        }
        finally
        {
            IsBusy = false;
            ConfirmPaymentCommand.NotifyCanExecuteChanged();
        }
    }

    public void SaveDraftState()
    {
        if (isOrderSubmitted)
            return;

        appState.SetDraftValue(DraftAddressKey, DeliveryAddress);
        appState.SetDraftValue(DraftDeliveryTypeKey, DeliveryType.ToString());
        appState.SetDraftValue(DraftPaymentMethodKey, PaymentMethod.ToString());
        appState.SaveState();
    }

    public void StartConnectivityMonitoring()
    {
        IsOffline = !connectivityService.IsConnected;
        connectivityService.ConnectivityChanged -= OnConnectivityChanged;
        connectivityService.ConnectivityChanged += OnConnectivityChanged;
        connectivityService.StartMonitoring();
    }

    public void StopConnectivityMonitoring()
    {
        connectivityService.ConnectivityChanged -= OnConnectivityChanged;
    }

    private void OnConnectivityChanged(object? sender, bool isConnected)
    {
        dispatcher.BeginInvokeOnMainThread(() =>
        {
            IsOffline = !isConnected;
        });
    }

    private void RestoreDraftState()
    {
        var savedAddress = appState.GetDraftValue(DraftAddressKey);
        var savedDeliveryType = appState.GetDraftValue(DraftDeliveryTypeKey);
        var savedPaymentMethod = appState.GetDraftValue(DraftPaymentMethodKey);

        if (!string.IsNullOrWhiteSpace(savedAddress))
            DeliveryAddress = savedAddress;
        if (Enum.TryParse<DeliveryType>(savedDeliveryType, true, out var parsedDelivery))
            DeliveryType = parsedDelivery;
        if (Enum.TryParse<PaymentMethod>(savedPaymentMethod, true, out var parsedPayment))
            PaymentMethod = parsedPayment;

        if (!string.IsNullOrWhiteSpace(savedAddress) || !string.IsNullOrWhiteSpace(savedDeliveryType))
            return;

        var raw = settings.GetString(LegacyDraftPreferenceKey, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
            return;

        try
        {
            var draft = JsonSerializer.Deserialize<CheckoutDraft>(raw);
            if (draft is null)
                return;

            if (!string.IsNullOrWhiteSpace(draft.DeliveryAddress))
                DeliveryAddress = draft.DeliveryAddress;
            DeliveryType = draft.DeliveryType;
            PaymentMethod = draft.PaymentMethod;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Corrupt checkout draft state cleared");
            settings.Remove(LegacyDraftPreferenceKey);
        }
    }

    private void SelectAddress(UserAddress? address)
    {
        if (address is null)
            return;

        DeliveryAddress = address.Text;
    }

    private void SelectDeliveryType(string? typeName)
    {
        if (Enum.TryParse<DeliveryType>(typeName, true, out var parsed))
            DeliveryType = parsed;
    }

    private void SelectPaymentMethod(string? methodName)
    {
        if (Enum.TryParse<PaymentMethod>(methodName, true, out var parsed))
            PaymentMethod = parsed;
    }

    private bool CanConfirmPayment() =>
        !IsBusy &&
        ItemsCount > 0 &&
        appState.SelectedPizzeria is not null &&
        (DeliveryType == DeliveryType.Pickup || !string.IsNullOrWhiteSpace(DeliveryAddress));

    private async Task ConfirmPaymentAsync()
    {
        if (!CanConfirmPayment())
            return;

        if (appState.SelectedPizzeria is null)
        {
            logger.LogWarning("Checkout blocked because no pizzeria is selected");
            ErrorMessage = "Оберіть піцерію перед оформленням замовлення.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        ConfirmPaymentCommand.NotifyCanExecuteChanged();
        try
        {
            var cartItems = await cartRepository.GetAllAsync();
            if (cartItems.Count == 0)
            {
                logger.LogWarning("Checkout aborted because cart is empty");
                ErrorMessage = "Кошик порожній.";
                return;
            }

            var profile = await userProfileRepository.GetCurrentAsync();
            var order = new Order
            {
                OrderNumber = $"PD-{Random.Shared.Next(1000, 9999)}",
                UserId = profile?.Id ?? 0,
                PizzeriaId = appState.SelectedPizzeria.Id,
                Status = OrderStatus.Preparing,
                DeliveryType = DeliveryType,
                PaymentMethod = PaymentMethod,
                DeliveryAddress = DeliveryType == DeliveryType.Pickup ? "Самовивіз із піцерії" : DeliveryAddress.Trim(),
                DeliveryFee = 0m,
                TotalAmount = cartItems.Sum(i => i.CalculateSubtotal()),
                CreatedAtUtc = DateTime.UtcNow
            };

            var orderItems = cartItems.Select(ci => new OrderItem
            {
                ProductId = ci.ProductId,
                ProductName = ci.ProductName,
                Quantity = ci.Quantity,
                UnitPrice = ci.UnitPrice,
                WeightGrams = 455,
                SizeName = string.IsNullOrWhiteSpace(ci.OptionsSummary) ? "Standard" : ci.OptionsSummary,
                OptionsSummary = ci.OptionsSummary
            }).ToList();

            await orderRepository.SaveOrderWithItemsAsync(order, orderItems);
            if (DeliveryType == DeliveryType.Delivery)
                await PersistRecentAddressAsync(profile?.Id ?? 0, DeliveryAddress.Trim());

            await cartRepository.ClearAsync();
            isOrderSubmitted = true;
            appState.ClearDraftPrefix(DraftPrefix);
            appState.SaveState();
            settings.Remove(LegacyDraftPreferenceKey);

            IsOffline = !connectivityService.IsConnected;
            await syncService.EnqueueAsync("Order", "Insert", new
            {
                order.Id,
                order.OrderNumber,
                order.TotalAmount,
                order.DeliveryType,
                order.PaymentMethod,
                UpdatedAt = DateTime.UtcNow
            });

            logger.LogInformation(
                "Order {OrderNumber} placed successfully (Total={TotalAmount}, DeliveryType={DeliveryType}, PaymentMethod={PaymentMethod})",
                order.OrderNumber,
                order.TotalAmount,
                order.DeliveryType,
                order.PaymentMethod);

            crashReporting?.TrackEvent(CrashEventNames.OrderPlaced, new Dictionary<string, string>
            {
                ["order_number"] = order.OrderNumber,
                ["delivery_type"] = order.DeliveryType.ToString(),
                ["payment_method"] = order.PaymentMethod.ToString(),
                ["items_count"] = orderItems.Count.ToString()
            });

            await navigation.PopToRootAsync(false);
            await navigation.GoToAsync("///history");
            await navigation.PopToRootAsync(false);
            await navigation.GoToAsync($"orderdetail?id={order.Id}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Order checkout failed");
            crashReporting?.CaptureException(ex, "CheckoutViewModel.ConfirmPaymentAsync");
            ErrorMessage = "Не вдалося оформити замовлення. Спробуйте ще раз.";
            SetReportableErrorContext("Checkout: ConfirmPaymentAsync");
        }
        finally
        {
            IsBusy = false;
            ConfirmPaymentCommand.NotifyCanExecuteChanged();
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

        await bugReportService.PromptAndSendAsync(reportableErrorContext ?? "Оплата замовлення");
    }

    private async Task PersistRecentAddressAsync(int userId, string addressText)
    {
        if (string.IsNullOrWhiteSpace(addressText))
            return;

        var existing = SavedAddresses.FirstOrDefault(a =>
            string.Equals(a.Text.Trim(), addressText, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.LastUsedAt = DateTime.UtcNow;
            await addressRepository.SaveAsync(existing);
            return;
        }

        var address = new UserAddress
        {
            UserId = userId,
            Label = "Доставка",
            Text = addressText,
            LastUsedAt = DateTime.UtcNow
        };
        await addressRepository.SaveAsync(address);
    }

    private sealed record CheckoutDraft(string DeliveryAddress, DeliveryType DeliveryType, PaymentMethod PaymentMethod);
}
