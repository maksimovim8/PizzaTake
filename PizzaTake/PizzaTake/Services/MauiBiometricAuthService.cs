using Microsoft.Extensions.Logging;
#if ANDROID
using AndroidX.Biometric;
using AndroidX.Core.Content;
using AndroidX.Fragment.App;
#elif WINDOWS
using Windows.Security.Credentials.UI;
#elif IOS || MACCATALYST
using Foundation;
using LocalAuthentication;
#endif

namespace PizzaTake.Services;

/// <summary>
/// Native cross-platform biometric authentication service:
/// AndroidX BiometricPrompt on Android, Windows Hello UserConsentVerifier on Windows, LAContext on Apple platforms.
/// </summary>
public sealed class MauiBiometricAuthService : IBiometricAuthService
{
    private readonly ILogger<MauiBiometricAuthService>? logger;

    public MauiBiometricAuthService(ILogger<MauiBiometricAuthService>? logger = null)
    {
        this.logger = logger;
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
#if ANDROID
            var context = Platform.CurrentActivity ?? Android.App.Application.Context;
            if (context is null)
                return false;

            var biometricManager = BiometricManager.From(context);
            var status = biometricManager.CanAuthenticate(
                BiometricManager.Authenticators.BiometricWeak | BiometricManager.Authenticators.BiometricStrong);
            return status == BiometricManager.BiometricSuccess;
#elif WINDOWS
            var availability = await UserConsentVerifier.CheckAvailabilityAsync();
            return availability == UserConsentVerifierAvailability.Available;
#elif IOS || MACCATALYST
            using var context = new LAContext();
            return context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthenticationWithBiometrics, out _);
#else
            await Task.CompletedTask;
            return false;
#endif
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Biometric availability check failed.");
            return false;
        }
    }

    public async Task<BiometricAuthResult> AuthenticateAsync(string reason)
    {
        var promptReason = string.IsNullOrWhiteSpace(reason)
            ? "Підтвердіть особу для входу в PizzaTake"
            : reason.Trim();

        try
        {
#if ANDROID
            return await AuthenticateAndroidAsync(promptReason);
#elif WINDOWS
            var availability = await UserConsentVerifier.CheckAvailabilityAsync();
            if (availability != UserConsentVerifierAvailability.Available)
            {
                logger?.LogWarning("Windows Hello biometric auth unavailable: {Status}.", availability);
                return new BiometricAuthResult(false, "Біометрія (Windows Hello) недоступна або не налаштована на цьому пристрої.");
            }

            var verification = await UserConsentVerifier.RequestVerificationAsync(promptReason);
            if (verification == UserConsentVerificationResult.Verified)
                return new BiometricAuthResult(true);

            logger?.LogWarning("Windows Hello verification failed with result: {Result}.", verification);
            return new BiometricAuthResult(false, verification == UserConsentVerificationResult.Canceled
                ? "Біометричну перевірку скасовано користувачем."
                : "Не вдалося підтвердити особу за допомогою Windows Hello.");
#elif IOS || MACCATALYST
            using var context = new LAContext();
            if (!context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthenticationWithBiometrics, out var error))
                return new BiometricAuthResult(false, error?.LocalizedDescription ?? "Біометрія недоступна на цьому пристрої.");

            var (success, evalError) = await context.EvaluatePolicyAsync(
                LAPolicy.DeviceOwnerAuthenticationWithBiometrics, promptReason);
            return success
                ? new BiometricAuthResult(true)
                : new BiometricAuthResult(false, evalError?.LocalizedDescription ?? "Біометричну перевірку не пройдено.");
#else
            await Task.CompletedTask;
            return new BiometricAuthResult(false, "Біометрія не підтримується на цій платформі.");
#endif
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Biometric authentication threw an unexpected exception.");
            return new BiometricAuthResult(false, "Помилка біометричної автентифікації. Скористайтеся входом за SMS.");
        }
    }

#if ANDROID
    private async Task<BiometricAuthResult> AuthenticateAndroidAsync(string reason)
    {
        if (Platform.CurrentActivity is not FragmentActivity activity)
        {
            logger?.LogWarning("Android biometric auth failed: current activity is not a FragmentActivity.");
            return new BiometricAuthResult(false, "Не вдалося відкрити системний діалог біометрії.");
        }

        var tcs = new TaskCompletionSource<BiometricAuthResult>();

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            try
            {
                var executor = ContextCompat.GetMainExecutor(activity);
                if (executor is null)
                {
                    tcs.TrySetResult(new BiometricAuthResult(false, "Не вдалося ініціалізувати біометричний сервіс."));
                    return;
                }

                var callback = new AndroidBiometricCallback(tcs, logger);
                var prompt = new BiometricPrompt(activity, executor, callback);

                var promptInfo = new BiometricPrompt.PromptInfo.Builder()
                    .SetTitle("Вхід у PizzaTake")
                    .SetSubtitle(reason)
                    .SetNegativeButtonText("Скасувати")
                    .SetAllowedAuthenticators(
                        BiometricManager.Authenticators.BiometricWeak | BiometricManager.Authenticators.BiometricStrong)
                    .Build();

                prompt.Authenticate(promptInfo);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to display Android BiometricPrompt.");
                tcs.TrySetResult(new BiometricAuthResult(false, "Біометрія недоступна на цьому пристрої."));
            }
        });

        return await tcs.Task;
    }

    private sealed class AndroidBiometricCallback : BiometricPrompt.AuthenticationCallback
    {
        private readonly TaskCompletionSource<BiometricAuthResult> tcs;
        private readonly ILogger? logger;

        public AndroidBiometricCallback(TaskCompletionSource<BiometricAuthResult> tcs, ILogger? logger)
        {
            this.tcs = tcs;
            this.logger = logger;
        }

        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult result)
        {
            base.OnAuthenticationSucceeded(result);
            tcs.TrySetResult(new BiometricAuthResult(true));
        }

        public override void OnAuthenticationError(int errorCode, Java.Lang.ICharSequence errString)
        {
            base.OnAuthenticationError(errorCode, errString);
            var message = errString?.ToString() ?? "Біометричну перевірку скасовано.";
            logger?.LogWarning("Android biometric error ({Code}): {Message}", errorCode, message);
            tcs.TrySetResult(new BiometricAuthResult(false, message));
        }

        public override void OnAuthenticationFailed()
        {
            base.OnAuthenticationFailed();
            logger?.LogWarning("Android biometric recognition did not match.");
        }
    }
#endif
}
