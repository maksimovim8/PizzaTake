namespace PizzaTake.Core.Services;

/// <summary>
/// Normalizes Ukrainian phone input to the strict <c>+380XXXXXXXXX</c> format (max 9 subscriber digits).
/// </summary>
public static class PhoneNumberNormalizer
{
    public const string CountryPrefix = "+380";
    public const int MaxSubscriberDigits = 9;
    public const int MaxTotalLength = 13;

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
