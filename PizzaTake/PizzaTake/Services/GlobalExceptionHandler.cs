using Sentry;
using Serilog;

namespace PizzaTake.Services;

/// <summary>
/// Registers process-wide and platform unhandled exception handlers forwarding to Serilog and Sentry.
/// </summary>
public static class GlobalExceptionHandler
{
    private static int isRegistered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref isRegistered, 1) == 1)
            return;

        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public static void HandlePlatformUnhandledException(Exception? exception, string source)
    {
        var ex = exception ?? new InvalidOperationException($"Unknown platform exception from {source}.");
        Log.Fatal(ex, "Unhandled platform exception from {Source}", source);
        CaptureAndFlushFatal(ex, source);
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        var exception = args.ExceptionObject as Exception
            ?? new InvalidOperationException($"Unhandled non-Exception error: {args.ExceptionObject}");

        Log.Fatal(
            exception,
            "Unhandled AppDomain exception (IsTerminating={IsTerminating})",
            args.IsTerminating);

        CaptureAndFlushFatal(exception, "AppDomain.UnhandledException");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        Log.Error(args.Exception, "Unobserved background task exception captured and marked as observed");

        try
        {
            SentrySdk.CaptureException(args.Exception, scope =>
            {
                scope.SetTag("handler", "TaskScheduler.UnobservedTaskException");
            });
        }
        catch (Exception sentryEx)
        {
            Log.Warning(sentryEx, "Failed to forward unobserved task exception to Sentry");
        }

        args.SetObserved();
    }

    private static void CaptureAndFlushFatal(Exception exception, string handlerTag)
    {
        try
        {
            SentrySdk.CaptureException(exception, scope =>
            {
                scope.Level = SentryLevel.Fatal;
                scope.SetTag("handler", handlerTag);
            });
            SentrySdk.Flush(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Never throw from a fatal exception handler.
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
