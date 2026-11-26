using Microsoft.Maui.Controls;
using PizzaTake.Core.Services;

namespace PizzaTake.Behaviors;

/// <summary>
/// Restricts phone <see cref="Entry"/> input to the Ukrainian mobile format <c>+380XXXXXXXXX</c>
/// (mandatory <c>+380</c> prefix, digits only, maximum 9 subscriber digits / 13 characters total),
/// while gracefully normalizing pasted formatted numbers such as <c>099 000 00 01</c>.
/// </summary>
public sealed class PhoneInputBehavior : Behavior<Entry>
{
    public const string CountryPrefix = PhoneNumberNormalizer.CountryPrefix;
    public const int MaxSubscriberDigits = PhoneNumberNormalizer.MaxSubscriberDigits;
    public const int MaxTotalLength = PhoneNumberNormalizer.MaxTotalLength;

    private bool isUpdating;

    protected override void OnAttachedTo(Entry bindable)
    {
        base.OnAttachedTo(bindable);
        bindable.TextChanged += OnEntryTextChanged;

        var normalized = Normalize(bindable.Text);
        if (!string.Equals(bindable.Text, normalized, StringComparison.Ordinal))
        {
            isUpdating = true;
            try
            {
                bindable.Text = normalized;
                bindable.CursorPosition = normalized.Length;
            }
            finally
            {
                isUpdating = false;
            }
        }
    }

    protected override void OnDetachingFrom(Entry bindable)
    {
        bindable.TextChanged -= OnEntryTextChanged;
        base.OnDetachingFrom(bindable);
    }

    private void OnEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (isUpdating || sender is not Entry entry)
            return;

        var oldText = e.OldTextValue ?? string.Empty;
        var raw = e.NewTextValue ?? string.Empty;

        // Prevent deleting a single character inside the locked "+380" prefix while subscriber digits exist.
        if (oldText.StartsWith(CountryPrefix, StringComparison.Ordinal) &&
            !raw.StartsWith(CountryPrefix, StringComparison.Ordinal) &&
            oldText.Length - raw.Length == 1 &&
            raw.Length >= CountryPrefix.Length)
        {
            isUpdating = true;
            try
            {
                entry.Text = oldText;
                entry.CursorPosition = CountryPrefix.Length;
            }
            finally
            {
                isUpdating = false;
            }
            return;
        }

        var normalized = Normalize(raw);
        if (string.Equals(raw, normalized, StringComparison.Ordinal))
            return;

        isUpdating = true;
        try
        {
            entry.Text = normalized;
            entry.CursorPosition = normalized.Length;
        }
        finally
        {
            isUpdating = false;
        }
    }

    public static string Normalize(string? raw) => PhoneNumberNormalizer.Normalize(raw);
}
