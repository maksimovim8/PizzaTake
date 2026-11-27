using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PizzaTake.Core.Services;

namespace PizzaTake.Services.Platform;

/// <summary>
/// MAUI runtime permission service supporting status checks, rationale dialogs, and non-crashing denial logging.
/// </summary>
public sealed class MauiPermissionService : IPermissionService
{
    private readonly ILogger<MauiPermissionService> logger;

    public MauiPermissionService(ILogger<MauiPermissionService>? logger = null)
    {
        this.logger = logger ?? NullLogger<MauiPermissionService>.Instance;
    }

    public Task<bool> IsGrantedAsync(AppPermission permission) => permission switch
    {
        AppPermission.Camera => CheckGrantedAsync<Permissions.Camera>(permission),
        AppPermission.LocationWhenInUse => CheckGrantedAsync<Permissions.LocationWhenInUse>(permission),
        _ => Task.FromResult(false)
    };

    public Task<bool> EnsureGrantedAsync(AppPermission permission, string rationaleTitle, string rationaleMessage) => permission switch
    {
        AppPermission.Camera => EnsureGrantedCoreAsync<Permissions.Camera>(permission, rationaleTitle, rationaleMessage),
        AppPermission.LocationWhenInUse => EnsureGrantedCoreAsync<Permissions.LocationWhenInUse>(permission, rationaleTitle, rationaleMessage),
        _ => Task.FromResult(false)
    };

    private async Task<bool> CheckGrantedAsync<TPermission>(AppPermission permission)
        where TPermission : Permissions.BasePermission, new()
    {
        try
        {
            var status = await MainThread.InvokeOnMainThreadAsync(Permissions.CheckStatusAsync<TPermission>);
            return status == PermissionStatus.Granted;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check status for permission {Permission}", permission);
            return false;
        }
    }

    private async Task<bool> EnsureGrantedCoreAsync<TPermission>(
        AppPermission permission,
        string rationaleTitle,
        string rationaleMessage)
        where TPermission : Permissions.BasePermission, new()
    {
        try
        {
            var status = await MainThread.InvokeOnMainThreadAsync(Permissions.CheckStatusAsync<TPermission>);
            if (status == PermissionStatus.Granted)
                return true;

            var shouldShowRationale = await MainThread.InvokeOnMainThreadAsync(Permissions.ShouldShowRationale<TPermission>);
            if (shouldShowRationale && Shell.Current is not null)
            {
                logger.LogInformation("Showing permission rationale dialog for {Permission}", permission);
                var proceed = await MainThread.InvokeOnMainThreadAsync(() =>
                    Shell.Current.DisplayAlertAsync(
                        rationaleTitle,
                        rationaleMessage,
                        "Надати дозвіл",
                        "Скасувати"));

                if (!proceed)
                {
                    logger.LogWarning("User declined rationale dialog for permission {Permission}", permission);
                    return false;
                }
            }

            status = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<TPermission>);
            if (status == PermissionStatus.Granted)
            {
                logger.LogInformation("Permission {Permission} granted by user", permission);
                return true;
            }

            logger.LogWarning("Permission {Permission} denied with status {Status}", permission, status);
            return false;
        }
        catch (FeatureNotSupportedException ex)
        {
            logger.LogWarning(ex, "Permission {Permission} is not supported on this platform", permission);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unexpected error while requesting permission {Permission}", permission);
            return false;
        }
    }
}
