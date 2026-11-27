using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.ViewModels;

/// <summary>
/// ViewModel for phone-based OTP authentication, biometric unlock of saved sessions, and SMS verification modal flow.
/// </summary>
public sealed class AuthViewModel : FormFeedbackViewModel, IQueryParameterReceiver
{
    private readonly IAuthService authService;
    private readonly IBiometricAuthService biometricAuthService;
    private readonly INavigationService navigation;
    private readonly ILogger<AuthViewModel> logger;

    private string phoneInput = PhoneNumberNormalizer.CountryPrefix;
    private string smsOtpCode = string.Empty;
    private bool isPolicyAccepted;
    private bool isSmsPopupVisible;
    private bool isBiometricLoginVisible;
    private bool isSessionLocked;
    private string biometricStatusMessage = string.Empty;
    private string? nextRoute;

    public AuthViewModel(
        IAuthService authService,
        IBiometricAuthService biometricAuthService,
        INavigationService navigation,
        IBugReportService? bugReportService = null,
        ILogger<AuthViewModel>? logger = null)
        : base(bugReportService, "Авторизація")
    {
        this.authService = authService;
        this.biometricAuthService = biometricAuthService;
        this.navigation = navigation;
        this.logger = logger ?? NullLogger<AuthViewModel>.Instance;
        Title = "Авторизація";

        InitializeCommand = new AsyncRelayCommand(InitializeAsync);
        RequestSmsCommand = new AsyncRelayCommand(RequestSmsAsync, CanRequestSms);
        VerifySmsCommand = new AsyncRelayCommand(VerifySmsAsync, CanVerifySms);
        BiometricLoginCommand = new AsyncRelayCommand(BiometricLoginAsync, () => !IsBusy);
        CloseSmsPopupCommand = new RelayCommand(() => IsSmsPopupVisible = false);
        GoBackCommand = new AsyncRelayCommand(() => navigation.GoBackAsync());
    }

    public string PhoneInput
    {
        get => phoneInput;
        set => SetInput(ref phoneInput, PhoneNumberNormalizer.Normalize(value), RequestSmsCommand);
    }

    public string SmsOtpCode { get => smsOtpCode; set => SetInput(ref smsOtpCode, value, VerifySmsCommand); }
    public bool IsPolicyAccepted { get => isPolicyAccepted; set { if (SetProperty(ref isPolicyAccepted, value)) RequestSmsCommand.NotifyCanExecuteChanged(); } }

    public bool IsSmsPopupVisible
    {
        get => isSmsPopupVisible;
        set { if (SetProperty(ref isSmsPopupVisible, value)) { OnPropertyChanged(nameof(HasMainError)); OnPropertyChanged(nameof(CanReportMainError)); } }
    }

    public bool IsBiometricLoginVisible { get => isBiometricLoginVisible; private set => SetProperty(ref isBiometricLoginVisible, value); }

    public bool IsSessionLocked
    {
        get => isSessionLocked;
        private set { if (SetProperty(ref isSessionLocked, value)) { OnPropertyChanged(nameof(HeaderTitle)); OnPropertyChanged(nameof(SubtitleText)); } }
    }

    public string HeaderTitle => IsSessionLocked ? "Сесію заблоковано" : "Приєднуйся до";

    public string SubtitleText => IsSessionLocked
        ? "Підтвердіть особу за біометрією або увійдіть повторно за номером телефону"
        : "Введіть номер телефону, щоб увійти у додаток";

    public string BiometricStatusMessage
    {
        get => biometricStatusMessage;
        private set { if (SetProperty(ref biometricStatusMessage, value)) OnPropertyChanged(nameof(HasBiometricStatusMessage)); }
    }

    public bool HasBiometricStatusMessage => !string.IsNullOrEmpty(BiometricStatusMessage);
    public bool HasMainError => HasError && !IsSmsPopupVisible;
    public bool CanReportMainError => CanReportError && !IsSmsPopupVisible;

    public IAsyncRelayCommand InitializeCommand { get; }
    public IAsyncRelayCommand RequestSmsCommand { get; }
    public IAsyncRelayCommand VerifySmsCommand { get; }
    public IAsyncRelayCommand BiometricLoginCommand { get; }
    public IRelayCommand CloseSmsPopupCommand { get; }
    public IAsyncRelayCommand GoBackCommand { get; }

    protected override void OnErrorMessageChanged()
    {
        OnPropertyChanged(nameof(HasMainError));
        OnPropertyChanged(nameof(CanReportMainError));
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("next", out var nextObj)) nextRoute = nextObj?.ToString();
        if (query.TryGetValue("locked", out var lockedObj) && bool.TryParse(lockedObj?.ToString(), out var locked))
            IsSessionLocked = locked;
    }

    public async Task InitializeAsync()
    {
        try
        {
            var hasSavedToken = await authService.HasSavedCredentialsAsync();
            IsSessionLocked = hasSavedToken && !authService.IsAuthenticated;
            if (!hasSavedToken)
            {
                IsBiometricLoginVisible = false;
                BiometricStatusMessage = string.Empty;
                return;
            }

            var biometricAvailable = await biometricAuthService.IsAvailableAsync();
            IsBiometricLoginVisible = biometricAvailable;
            BiometricStatusMessage = biometricAvailable
                ? string.Empty
                : "Біометрія недоступна на цьому пристрої — увійдіть за номером телефону.";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to initialize biometric state on AuthPage.");
            IsBiometricLoginVisible = false;
            BiometricStatusMessage = "Біометрія недоступна на цьому пристрої.";
        }
    }

    private bool CanRequestSms() => IsPolicyAccepted && !IsBusy && HttpApiClient.IsValidPhone(PhoneInput ?? string.Empty);

    private bool CanVerifySms() =>
        !IsBusy && !string.IsNullOrWhiteSpace(SmsOtpCode) &&
        SmsOtpCode.Trim() is { Length: 4 } trimmed && trimmed.All(char.IsDigit);

    private async Task RequestSmsAsync()
    {
        if (!CanRequestSms()) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        RefreshCommandStates();
        try
        {
            if (!await authService.SendOtpAsync(PhoneInput.Trim()))
            {
                logger.LogWarning("OTP SMS request failed in AuthViewModel");
                ErrorMessage = "Не вдалося надіслати код. Введіть номер у форматі +380XXXXXXXXX.";
                return;
            }

            SmsOtpCode = string.Empty;
            IsSmsPopupVisible = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error requesting OTP SMS code");
            ErrorMessage = "Виникла помилка під час надсилання коду.";
            SetReportableErrorContext("Auth: RequestSmsAsync");
        }
        finally { IsBusy = false; RefreshCommandStates(); }
    }

    private async Task VerifySmsAsync()
    {
        if (!CanVerifySms()) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        RefreshCommandStates();
        try
        {
            if (!await authService.VerifyOtpAsync(PhoneInput.Trim(), SmsOtpCode.Trim()))
            {
                logger.LogWarning("OTP SMS code verification failed in AuthViewModel");
                ErrorMessage = "Невірний код підтвердження. Введіть коректний 4-значний код.";
                return;
            }

            logger.LogInformation("OTP verification succeeded; navigating to {NextRoute}", nextRoute ?? "..");
            IsSmsPopupVisible = false;
            await NavigateAfterAuthAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error verifying OTP SMS code");
            ErrorMessage = "Не вдалося перевірити код. Спробуйте ще раз.";
            SetReportableErrorContext("Auth: VerifySmsAsync");
        }
        finally { IsBusy = false; RefreshCommandStates(); }
    }

    private async Task BiometricLoginAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        RefreshCommandStates();
        try
        {
            if (!await authService.HasSavedCredentialsAsync())
            { ErrorMessage = "Збережений токен відсутній. Увійдіть за номером телефону."; IsBiometricLoginVisible = false; return; }

            if (!await biometricAuthService.IsAvailableAsync())
            {
                logger.LogWarning("Biometric login attempted, but biometrics are not available on this device.");
                ErrorMessage = "Біометрична автентифікація недоступна на цьому пристрої.";
                IsBiometricLoginVisible = false;
                return;
            }

            var authResult = await biometricAuthService.AuthenticateAsync("Підтвердіть особу для повторного входу в PizzaTake");
            if (!authResult.IsSuccess) { ErrorMessage = authResult.ErrorMessage ?? "Біометричну автентифікацію не пройдено."; return; }

            if (!await authService.RestoreSavedSessionAsync())
            { ErrorMessage = "Не вдалося відновити сесію. Увійдіть за SMS-кодом."; return; }

            logger.LogInformation("Biometric login succeeded; session restored.");
            await NavigateAfterAuthAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Biometric login failed unexpectedly.");
            ErrorMessage = "Помилка біометричного входу. Увійдіть за SMS-кодом.";
            SetReportableErrorContext("Auth: BiometricLoginAsync");
        }
        finally { IsBusy = false; RefreshCommandStates(); }
    }

    private Task NavigateAfterAuthAsync() =>
        !string.IsNullOrWhiteSpace(nextRoute) ? navigation.GoToAsync($"../{nextRoute}") : navigation.GoBackAsync();

    private void RefreshCommandStates()
    {
        RequestSmsCommand.NotifyCanExecuteChanged();
        VerifySmsCommand.NotifyCanExecuteChanged();
        BiometricLoginCommand.NotifyCanExecuteChanged();
    }
}
