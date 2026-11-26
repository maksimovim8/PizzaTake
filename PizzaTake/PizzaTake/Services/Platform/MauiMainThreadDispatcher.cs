using PizzaTake.Core.Services;

namespace PizzaTake.Services.Platform;

public sealed class MauiMainThreadDispatcher : IMainThreadDispatcher
{
    public void BeginInvokeOnMainThread(Action action) =>
        MainThread.BeginInvokeOnMainThread(action);

    public Task InvokeOnMainThreadAsync(Action action) =>
        MainThread.InvokeOnMainThreadAsync(action);

    public Task InvokeOnMainThreadAsync(Func<Task> func) =>
        MainThread.InvokeOnMainThreadAsync(func);
}
