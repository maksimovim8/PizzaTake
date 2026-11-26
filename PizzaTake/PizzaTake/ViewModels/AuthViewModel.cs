using System.Text.RegularExpressions;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

/// <summary>
/// ViewModel for phone-based OTP authentication and SMS verification modal flow.
/// </summary>
public sealed class AuthViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IAuthService authService;
    private readonly ILogger<AuthViewModel> logger;

    private string phoneInput = "+380";
    private string smsOtpCode = string.Empty;
    private bool isPolicyAccepted;
    private bool isSmsPopupVisible;
    private string errorMessage = string.Empty;
    private string? nextRoute;

    public AuthViewModel(IAuthService authService, ILogger<AuthViewModel>? logger = null)
    {
        this.authService = authService;
        this.logger = logger ?? NullLogger<AuthViewModel>.Instance;
        Title = "Авторизація";

        RequestSmsCommand = new Command(async () => await RequestSmsAsync(), CanRequestSms);
        VerifySmsCommand = new Command(async () => await VerifySmsAsync(), CanVerifySms);
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

    public ICommand RequestSmsCommand { get; }
    public ICommand VerifySmsCommand { get; }
    public ICommand CloseSmsPopupCommand { get; }
    public ICommand GoBackCommand { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("next", out var nextObj))
            nextRoute = nextObj?.ToString();
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
            ((Command)RequestSmsCommand).ChangeCanExecute();
            ((Command)VerifySmsCommand).ChangeCanExecute();
        }
    }

    private async Task VerifySmsAsync()
    {
        if (!CanVerifySms())
            return;

        IsBusy = true;
        ErrorMessage = string.Empty;
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
            if (!string.IsNullOrWhiteSpace(nextRoute))
                await Shell.Current.GoToAsync($"../{nextRoute}");
            else
                await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error verifying OTP SMS code");
            ErrorMessage = "Не вдалося перевірити код. Спробуйте ще раз.";
        }
        finally
        {
            IsBusy = false;
            ((Command)RequestSmsCommand).ChangeCanExecute();
            ((Command)VerifySmsCommand).ChangeCanExecute();
        }
    }
}
