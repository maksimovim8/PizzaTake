using System.Text.RegularExpressions;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

/// <summary>
/// ViewModel for phone-based OTP authentication, biometric unlock of saved sessions, and SMS verification modal flow.
/// </summary>
public sealed class AuthViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IAuthService authService;
    private readonly IBiometricAuthService biometricAuthService;
    private readonly ILogger<AuthViewModel> logger;

    private string phoneInput = "+380";
    private string smsOtpCode = string.Empty;
    private bool isPolicyAccepted;
    private bool isSmsPopupVisible;
    private bool isBiometricLoginVisible;
    private bool isSessionLocked;
    private string biometricStatusMessage = string.Empty;
    private string errorMessage = string.Empty;
    private string? nextRoute;

    public AuthViewModel(
        IAuthService authService,
        IBiometricAuthService biometricAuthService,
        ILogger<AuthViewModel>? logger = null)
    {
        this.authService = authService;
        this.biometricAuthService = biometricAuthService;
        this.logger = logger ?? NullLogger<AuthViewModel>.Instance;
        Title = "Авторизація";

        InitializeCommand = new Command(async () => await InitializeAsync());
        RequestSmsCommand = new Command(async () => await RequestSmsAsync(), CanRequestSms);
        VerifySmsCommand = new Command(async () => await VerifySmsAsync(), CanVerifySms);
        BiometricLoginCommand = new Command(async () => await BiometricLoginAsync(), () => !IsBusy);
        CloseSmsPopupCommand = new Command(() => IsSmsPopupVisible = false);
        GoBackCommand = new Command(async () => await Shell.Current.GoToAsync(".."));
    }

    public string PhoneInput
    {
        get => phoneInput;
        set
        {
            if (SetProperty(ref phoneInput, value))
            {
                ErrorMessage = string.Empty;
                ((Command)RequestSmsCommand).ChangeCanExecute();
            }
        }
    }

    public string SmsOtpCode
    {
        get => smsOtpCode;
        set
        {
            if (SetProperty(ref smsOtpCode, value))
            {
                ErrorMessage = string.Empty;
                ((Command)VerifySmsCommand).ChangeCanExecute();
            }
        }
    }

    public bool IsPolicyAccepted
    {
        get => isPolicyAccepted;
        set
        {
            if (SetProperty(ref isPolicyAccepted, value))
                ((Command)RequestSmsCommand).ChangeCanExecute();
        }
    }

    public bool IsSmsPopupVisible
    {
        get => isSmsPopupVisible;
        set
        {
            if (SetProperty(ref isSmsPopupVisible, value))
                OnPropertyChanged(nameof(HasMainError));
        }
    }

    public bool IsBiometricLoginVisible
    {
        get => isBiometricLoginVisible;
        private set => SetProperty(ref isBiometricLoginVisible, value);
    }

    public bool IsSessionLocked
    {
        get => isSessionLocked;
        private set
        {
            if (SetProperty(ref isSessionLocked, value))
            {
                OnPropertyChanged(nameof(HeaderTitle));
                OnPropertyChanged(nameof(SubtitleText));
            }
        }
    }

    public string HeaderTitle => IsSessionLocked ? "Сесію заблоковано" : "Приєднуйся до";

    public string SubtitleText => IsSessionLocked
        ? "Підтвердіть особу за біометрією або увійдіть повторно за номером телефону"
        : "Введіть номер телефону, щоб увійти у додаток";

    public string BiometricStatusMessage
    {
        get => biometricStatusMessage;
        private set
        {
            if (SetProperty(ref biometricStatusMessage, value))
                OnPropertyChanged(nameof(HasBiometricStatusMessage));
        }
    }

    public bool HasBiometricStatusMessage => !string.IsNullOrEmpty(BiometricStatusMessage);

    public string ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (SetProperty(ref errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(HasMainError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasMainError => HasError && !IsSmsPopupVisible;

    public ICommand InitializeCommand { get; }
    public ICommand RequestSmsCommand { get; }
    public ICommand VerifySmsCommand { get; }
    public ICommand BiometricLoginCommand { get; }
    public ICommand CloseSmsPopupCommand { get; }
    public ICommand GoBackCommand { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("next", out var nextObj))
            nextRoute = nextObj?.ToString();

        if (query.TryGetValue("locked", out var lockedObj) &&
            bool.TryParse(lockedObj?.ToString(), out var locked))
        {
            IsSessionLocked = locked;
        }
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
            logger?.LogWarning(ex, "Failed to initialize biometric state on AuthPage.");
            IsBiometricLoginVisible = false;
            BiometricStatusMessage = "Біометрія недоступна на цьому пристрої.";
        }
    }

    private bool CanRequestSms()
    {
        if (!IsPolicyAccepted || IsBusy)
            return false;

        return HttpApiClient.IsValidPhone(PhoneInput ?? string.Empty);
    }

    private bool CanVerifySms()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(SmsOtpCode))
            return false;

        var trimmed = SmsOtpCode.Trim();
        return trimmed.Length == 4 && trimmed.All(char.IsDigit);
    }

    private async Task RequestSmsAsync()
    {
        if (!CanRequestSms())
            return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        RefreshCommandStates();
        try
        {
            var sent = await authService.SendOtpAsync(PhoneInput.Trim());
            if (!sent)
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
        }
        finally
        {
            IsBusy = false;
            RefreshCommandStates();
        }
    }

    private async Task VerifySmsAsync()
    {
        if (!CanVerifySms())
            return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        RefreshCommandStates();
        try
        {
            var verified = await authService.VerifyOtpAsync(PhoneInput.Trim(), SmsOtpCode.Trim());
            if (!verified)
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
        }
        finally
        {
            IsBusy = false;
            RefreshCommandStates();
        }
    }

    private async Task BiometricLoginAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        RefreshCommandStates();
        try
        {
            var hasSavedToken = await authService.HasSavedCredentialsAsync();
            if (!hasSavedToken)
            {
                ErrorMessage = "Збережений токен відсутній. Увійдіть за номером телефону.";
                IsBiometricLoginVisible = false;
                return;
            }

            var isAvailable = await biometricAuthService.IsAvailableAsync();
            if (!isAvailable)
            {
                logger?.LogWarning("Biometric login attempted, but biometrics are not available on this device.");
                ErrorMessage = "Біометрична автентифікація недоступна на цьому пристрої.";
                IsBiometricLoginVisible = false;
                return;
            }

            var authResult = await biometricAuthService.AuthenticateAsync("Підтвердіть особу для повторного входу в PizzaTake");
            if (!authResult.IsSuccess)
            {
                ErrorMessage = authResult.ErrorMessage ?? "Біометричну автентифікацію не пройдено.";
                return;
            }

            var restored = await authService.RestoreSavedSessionAsync();
            if (!restored)
            {
                ErrorMessage = "Не вдалося відновити сесію. Увійдіть за SMS-кодом.";
                return;
            }

            logger?.LogInformation("Biometric login succeeded; session restored.");
            await NavigateAfterAuthAsync();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Biometric login failed unexpectedly.");
            ErrorMessage = "Помилка біометричного входу. Увійдіть за SMS-кодом.";
        }
        finally
        {
            IsBusy = false;
            RefreshCommandStates();
        }
    }

    private async Task NavigateAfterAuthAsync()
    {
        if (!string.IsNullOrWhiteSpace(nextRoute))
            await Shell.Current.GoToAsync($"../{nextRoute}");
        else
            await Shell.Current.GoToAsync("..");
    }

    private void RefreshCommandStates()
    {
        ((Command)RequestSmsCommand).ChangeCanExecute();
        ((Command)VerifySmsCommand).ChangeCanExecute();
        ((Command)BiometricLoginCommand).ChangeCanExecute();
    }
}
