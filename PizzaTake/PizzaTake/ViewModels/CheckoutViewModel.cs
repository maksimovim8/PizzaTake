using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using PizzaTake.Models;
using PizzaTake.Repositories;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

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

    private string deliveryAddress = string.Empty;
    private DeliveryType deliveryType = DeliveryType.Delivery;
    private PaymentMethod paymentMethod = PaymentMethod.CardOnline;
    private decimal totalAmount;
    private int itemsCount;
    private bool isOffline;
    private string errorMessage = string.Empty;
    private bool isOrderSubmitted;

    public CheckoutViewModel(
        ICartRepository cartRepository,
        IOrderRepository orderRepository,
        IAddressRepository addressRepository,
        IUserProfileRepository userProfileRepository,
        ISyncService syncService,
        IAppStateService appState)
    {
        this.cartRepository = cartRepository;
        this.orderRepository = orderRepository;
        this.addressRepository = addressRepository;
        this.userProfileRepository = userProfileRepository;
        this.syncService = syncService;
        this.appState = appState;
        Title = "Оплата замовлення";

        LoadCheckoutCommand = new Command(async () => await LoadCheckoutAsync());
        SelectAddressCommand = new Command<UserAddress?>(SelectAddress);
        SelectDeliveryTypeCommand = new Command<string?>(SelectDeliveryType);
        SelectPaymentMethodCommand = new Command<string?>(SelectPaymentMethod);
        ConfirmPaymentCommand = new Command(async () => await ConfirmPaymentAsync(), CanConfirmPayment);
        GoBackCommand = new Command(async () => await Shell.Current.GoToAsync(".."));
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
                ((Command)ConfirmPaymentCommand).ChangeCanExecute();
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
                ((Command)ConfirmPaymentCommand).ChangeCanExecute();
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
                ((Command)ConfirmPaymentCommand).ChangeCanExecute();
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
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public ICommand LoadCheckoutCommand { get; }
    public ICommand SelectAddressCommand { get; }
    public ICommand SelectDeliveryTypeCommand { get; }
    public ICommand SelectPaymentMethodCommand { get; }
    public ICommand ConfirmPaymentCommand { get; }
    public ICommand GoBackCommand { get; }

    public async Task LoadCheckoutAsync()
    {
        if (IsBusy)
            return;

        isOrderSubmitted = false;
        IsBusy = true;
        try
        {
            IsOffline = Connectivity.Current.NetworkAccess != NetworkAccess.Internet;

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
        }
        finally
        {
            IsBusy = false;
            ((Command)ConfirmPaymentCommand).ChangeCanExecute();
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

        var raw = Preferences.Default.Get(LegacyDraftPreferenceKey, string.Empty);
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
        catch (JsonException)
        {
            Preferences.Default.Remove(LegacyDraftPreferenceKey);
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
            ErrorMessage = "Оберіть піцерію перед оформленням замовлення.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var cartItems = await cartRepository.GetAllAsync();
            if (cartItems.Count == 0)
            {
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
            Preferences.Default.Remove(LegacyDraftPreferenceKey);

            IsOffline = Connectivity.Current.NetworkAccess != NetworkAccess.Internet;
            await syncService.EnqueueAsync("Order", "Insert", new
            {
                order.Id,
                order.OrderNumber,
                order.TotalAmount,
                order.DeliveryType,
                order.PaymentMethod,
                UpdatedAt = DateTime.UtcNow
            });

            // Checkout is pushed on top of the Cart tab. Pop it before leaving so the tab
            // cannot reopen the payment page when the user selects Cart later.
            await Shell.Current.Navigation.PopToRootAsync(false);
            await Shell.Current.GoToAsync("///history");
            await Shell.Current.Navigation.PopToRootAsync(false);
            await Shell.Current.GoToAsync($"orderdetail?id={order.Id}");
        }
        finally
        {
            IsBusy = false;
            ((Command)ConfirmPaymentCommand).ChangeCanExecute();
        }
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
