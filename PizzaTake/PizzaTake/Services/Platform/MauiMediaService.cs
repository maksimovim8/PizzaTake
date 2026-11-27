using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PizzaTake.Core.Services;

namespace PizzaTake.Services.Platform;

/// <summary>
/// MAUI camera and photo gallery service that persists selected images into AppDataDirectory/photos.
/// </summary>
public sealed class MauiMediaService : IMediaService
{
    private readonly ILogger<MauiMediaService> logger;

    public MauiMediaService(ILogger<MauiMediaService>? logger = null)
    {
        this.logger = logger ?? NullLogger<MauiMediaService>.Instance;
    }

    public bool IsCaptureSupported
    {
        get
        {
            try
            {
                return MediaPicker.Default.IsCaptureSupported;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to query MediaPicker.IsCaptureSupported");
                return false;
            }
        }
    }

    public async Task<MediaCaptureResult> CapturePhotoAsync(string filePrefix, CancellationToken ct = default)
    {
        if (!IsCaptureSupported)
        {
            logger.LogWarning("Camera capture is not supported on this device");
            return new MediaCaptureResult(
                null,
                HardwareFailureReason.FeatureNotSupported,
                "Камера не підтримується на цьому пристрої. Скористайтеся вибором із галереї.");
        }

        try
        {
            var photo = await MainThread.InvokeOnMainThreadAsync(() =>
                MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions
                {
                    Title = "Зробити фото якості замовлення"
                }));

            if (photo is null)
            {
                logger.LogInformation("Camera capture cancelled by user");
                return new MediaCaptureResult(null, HardwareFailureReason.Cancelled, "Зйомку фото скасовано.");
            }

            var savedPath = await CopyToAppPhotosAsync(photo, filePrefix, ct);
            logger.LogInformation("Captured photo saved to {PhotoPath}", savedPath);
            return new MediaCaptureResult(savedPath);
        }
        catch (PermissionException ex)
        {
            logger.LogWarning(ex, "Camera permission denied during photo capture");
            return new MediaCaptureResult(
                null,
                HardwareFailureReason.PermissionDenied,
                "Надайте дозвіл на використання камери в налаштуваннях пристрою.");
        }
        catch (FeatureNotSupportedException ex)
        {
            logger.LogWarning(ex, "Camera capture feature is not supported");
            return new MediaCaptureResult(
                null,
                HardwareFailureReason.FeatureNotSupported,
                "Камера недоступна на цьому пристрої.");
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "Camera capture operation cancelled");
            return new MediaCaptureResult(null, HardwareFailureReason.Cancelled, "Зйомку фото скасовано.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to capture photo");
            return new MediaCaptureResult(
                null,
                HardwareFailureReason.Error,
                "Не вдалося зробити фото. Спробуйте ще раз або оберіть з галереї.");
        }
    }

    public async Task<MediaCaptureResult> PickPhotoAsync(string filePrefix, CancellationToken ct = default)
    {
        try
        {
            var photos = await MainThread.InvokeOnMainThreadAsync(() =>
                MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
                {
                    Title = "Обрати фото з галереї",
                    SelectionLimit = 1
                }));

            var photo = photos?.FirstOrDefault();
            if (photo is null)
            {
                logger.LogInformation("Photo selection from gallery cancelled by user");
                return new MediaCaptureResult(null, HardwareFailureReason.Cancelled, "Вибір фото скасовано.");
            }

            var savedPath = await CopyToAppPhotosAsync(photo, filePrefix, ct);
            logger.LogInformation("Picked gallery photo saved to {PhotoPath}", savedPath);
            return new MediaCaptureResult(savedPath);
        }
        catch (PermissionException ex)
        {
            logger.LogWarning(ex, "Gallery permission denied while picking photo");
            return new MediaCaptureResult(
                null,
                HardwareFailureReason.PermissionDenied,
                "Надайте дозвіл на доступ до фотографій.");
        }
        catch (FeatureNotSupportedException ex)
        {
            logger.LogWarning(ex, "Photo gallery picking is not supported on this device");
            return new MediaCaptureResult(
                null,
                HardwareFailureReason.FeatureNotSupported,
                "Вибір фото з галереї не підтримується на цьому пристрої.");
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "Gallery photo pick cancelled");
            return new MediaCaptureResult(null, HardwareFailureReason.Cancelled, "Вибір фото скасовано.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to pick photo from gallery");
            return new MediaCaptureResult(
                null,
                HardwareFailureReason.Error,
                "Не вдалося обрати фото з галереї. Спробуйте ще раз.");
        }
    }

    private static async Task<string> CopyToAppPhotosAsync(FileResult sourceFile, string filePrefix, CancellationToken ct)
    {
        var photosDirectory = Path.Combine(FileSystem.AppDataDirectory, "photos");
        Directory.CreateDirectory(photosDirectory);

        var safePrefix = string.IsNullOrWhiteSpace(filePrefix) ? "report" : filePrefix.Trim();
        var rawExt = Path.GetExtension(sourceFile.FileName)?.ToLowerInvariant();
        var ext = rawExt is ".jpg" or ".jpeg" or ".png" or ".webp" ? rawExt : ".jpg";
        var fileName = $"{safePrefix}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid().ToString("N")[..6]}{ext}";
        var targetPath = Path.Combine(photosDirectory, fileName);

        await using var sourceStream = await sourceFile.OpenReadAsync();
        await using var targetStream = File.Create(targetPath);
        await sourceStream.CopyToAsync(targetStream, ct);

        return targetPath;
    }
}
