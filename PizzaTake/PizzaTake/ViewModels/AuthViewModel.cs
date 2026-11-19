using System.Text.RegularExpressions;
using System.Windows.Input;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

/// <summary>
/// ViewModel for phone-based OTP authentication and SMS verification modal flow.
/// </summary>
public sealed class AuthViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IAuthService authService;

    private string phoneInput = "+380";
    private string smsOtpCode = string.Empty;
    private bool isPolicyAccepted;
    private bool isSmsPopupVisible;
    private string errorMessage = string.Empty;
    private string? nextRoute;

    public AuthViewModel(IAuthService authService)
    {
        this.authService = authService;
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
        set => SetProperty(ref isSmsPopupVisible, value);
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

        var digits = Regex.Replace(PhoneInput ?? string.Empty, @"\D", string.Empty);
        return digits.Length >= 9;
    }

    private bool CanVerifySms() =>
        !IsBusy && !string.IsNullOrWhiteSpace(SmsOtpCode) && SmsOtpCode.Trim().Length >= 4;

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
                ErrorMessage = "Не вдалося надіслати код. Перевірте номер телефону.";
                return;
            }

            SmsOtpCode = string.Empty;
            IsSmsPopupVisible = true;
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
                ErrorMessage = "Невірний код підтвердження. Введіть 4 цифри (наприклад, 1234).";
                return;
            }

            IsSmsPopupVisible = false;
            if (!string.IsNullOrWhiteSpace(nextRoute))
                await Shell.Current.GoToAsync($"../{nextRoute}");
            else
                await Shell.Current.GoToAsync("..");
        }
        finally
        {
            IsBusy = false;
            ((Command)RequestSmsCommand).ChangeCanExecute();
            ((Command)VerifySmsCommand).ChangeCanExecute();
        }
    }
}
