using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PizzaTake.Services;

/// <summary>
/// Accelerometer shake sensor service with IsSupported check, lifecycle Start/Stop, and shake debouncing.
/// </summary>
public sealed class MauiShakeSensorService : IShakeSensorService
{
    private static readonly TimeSpan ShakeCooldown = TimeSpan.FromSeconds(2);

    private readonly ILogger<MauiShakeSensorService> logger;
    private readonly object syncRoot = new();
    private DateTime lastShakeUtc = DateTime.MinValue;
    private bool isSubscribed;

    public MauiShakeSensorService(ILogger<MauiShakeSensorService>? logger = null)
    {
        this.logger = logger ?? NullLogger<MauiShakeSensorService>.Instance;
    }

    public bool IsSupported
    {
        get
        {
            try
            {
                return Accelerometer.Default.IsSupported;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to query Accelerometer.IsSupported");
                return false;
            }
        }
    }

    public bool IsMonitoring
    {
        get
        {
            try
            {
                return Accelerometer.Default.IsMonitoring;
            }
            catch
            {
                return false;
            }
        }
    }

    public event EventHandler? ShakeDetected;

    public void Start()
    {
        if (!IsSupported)
        {
            logger.LogWarning("Accelerometer shake sensor is not supported on this device");
            return;
        }

        lock (syncRoot)
        {
            try
            {
                if (!isSubscribed)
                {
                    Accelerometer.Default.ShakeDetected += OnAccelerometerShakeDetected;
                    isSubscribed = true;
                }

                if (!Accelerometer.Default.IsMonitoring)
                {
                    Accelerometer.Default.Start(SensorSpeed.Game);
                    logger.LogDebug("Accelerometer shake monitoring started");
                }
            }
            catch (FeatureNotSupportedException ex)
            {
                logger.LogWarning(ex, "Accelerometer shake monitoring is not supported");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to start accelerometer shake monitoring");
            }
        }
    }

    public void Stop()
    {
        lock (syncRoot)
        {
            try
            {
                if (isSubscribed)
                {
                    Accelerometer.Default.ShakeDetected -= OnAccelerometerShakeDetected;
                    isSubscribed = false;
                }

                if (Accelerometer.Default.IsMonitoring)
                {
                    Accelerometer.Default.Stop();
                    logger.LogDebug("Accelerometer shake monitoring stopped");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to stop accelerometer shake monitoring");
            }
        }
    }

    private void OnAccelerometerShakeDetected(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        lock (syncRoot)
        {
            if (now - lastShakeUtc < ShakeCooldown)
                return;

            lastShakeUtc = now;
        }

        logger.LogInformation("Device shake detected by accelerometer sensor");
        ShakeDetected?.Invoke(this, EventArgs.Empty);
    }
}
