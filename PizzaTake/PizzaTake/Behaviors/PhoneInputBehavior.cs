using Microsoft.Maui.Controls;

namespace PizzaTake.Behaviors;

/// <summary>
/// Restricts phone <see cref="Entry"/> input to the Ukrainian mobile format <c>+380XXXXXXXXX</c>
/// (mandatory <c>+380</c> prefix, digits only, maximum 9 subscriber digits / 13 characters total),
/// while gracefully normalizing pasted formatted numbers such as <c>099 000 00 01</c>.
/// </summary>
public sealed class PhoneInputBehavior : Behavior<Entry>
{
    public const string CountryPrefix = "+380";
    public const int MaxSubscriberDigits = 9;
    public const int MaxTotalLength = 13;

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

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return CountryPrefix;

        Span<char> buffer = stackalloc char[Math.Min(raw.Length, 64)];
        var count = 0;
        foreach (var ch in raw)
        {
            if (ch is >= '0' and <= '9' && count < buffer.Length)
                buffer[count++] = ch;
        }

        if (count == 0)
            return CountryPrefix;

        var digits = new string(buffer[..count]);

        string subscriber;
        if (digits.StartsWith("380", StringComparison.Ordinal))
        {
            subscriber = digits[3..];
            // Handle pasting a full number onto the existing "+380" prefix.
            if (subscriber.StartsWith("380", StringComparison.Ordinal) && subscriber.Length >= 12)
                subscriber = subscriber[3..];
            else if (subscriber.StartsWith('0') && subscriber.Length == 10)
                subscriber = subscriber[1..];
        }
        else if (digits is "3" or "38" or "0")
        {
            subscriber = string.Empty;
        }
        else if (digits.StartsWith("80", StringComparison.Ordinal) && digits.Length >= 10)
        {
            subscriber = digits[2..];
        }
        else if (digits.StartsWith('0'))
        {
            subscriber = digits[1..];
        }
        else
        {
            subscriber = digits;
        }

        if (subscriber.Length > MaxSubscriberDigits)
            subscriber = subscriber[..MaxSubscriberDigits];

        return CountryPrefix + subscriber;
    }
}
