using Android.App;
using Android.Runtime;
using PizzaTake.Services.Platform;

namespace PizzaTake
{
    [Application]
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }

        public override void OnCreate()
        {
            base.OnCreate();
            AndroidEnvironment.UnhandledExceptionRaiser += OnAndroidUnhandledException;
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        private static void OnAndroidUnhandledException(object? sender, RaiseThrowableEventArgs args)
        {
            GlobalExceptionHandler.HandlePlatformUnhandledException(
                args.Exception,
                "AndroidEnvironment.UnhandledExceptionRaiser");
        }
    }
}
