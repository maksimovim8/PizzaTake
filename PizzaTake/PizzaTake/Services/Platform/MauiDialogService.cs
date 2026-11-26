using PizzaTake.Core.Services;

namespace PizzaTake.Services.Platform;

public sealed class MauiDialogService : IDialogService
{
    public Task AlertAsync(string title, string message, string cancel = "OK") =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Shell.Current is not null)
                await Shell.Current.DisplayAlertAsync(title, message, cancel);
        });

    public Task<bool> ConfirmAsync(string title, string message, string accept = "Так", string cancel = "Ні") =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Shell.Current is null)
                return false;

            return await Shell.Current.DisplayAlertAsync(title, message, accept, cancel);
        });

    public Task<string?> PromptAsync(
        string title,
        string message,
        string accept = "Надіслати",
        string cancel = "Скасувати",
        string? placeholder = null,
        int maxLength = -1) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Shell.Current is null)
                return null;

            return await Shell.Current.DisplayPromptAsync(
                title,
                message,
                accept: accept,
                cancel: cancel,
                placeholder: placeholder,
                maxLength: maxLength);
        });
}
