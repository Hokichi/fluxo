using System.Globalization;
using System.Text;

namespace Fluxo.Resources.Helpers;

public static class MoneyFormatUtility
{
    private const decimal ThousandDivisor = 1_000m;
    private const decimal MillionDivisor = 1_000_000m;

    public static string ToCompactText(object? value, CultureInfo culture)
    {
        if (!TryCreateCanonical(value, culture, out var canonical))
            return string.Empty;

        return ToCompactTextFromCanonical(canonical, culture);
    }

    public static string ToFullText(object? value, CultureInfo culture)
    {
        if (!TryCreateCanonical(value, culture, out var canonical))
            return string.Empty;

        return FormatCanonical(canonical, culture);
    }

    public static string ToCompactTextFromCanonical(string canonical, CultureInfo culture)
    {
        if (canonical.Length == 0)
            return string.Empty;

        var integerDigits = GetIntegerDigitCount(canonical);
        if (!TryParseCanonical(canonical, out var amount))
            return string.Empty;

        if (integerDigits >= 7)
            return FormatAbbreviated(amount / MillionDivisor, "M", culture);

        if (integerDigits >= 4)
            return FormatAbbreviated(amount / ThousandDivisor, "K", culture);

        return FormatCanonical(canonical, culture);
    }

    public static string BuildCanonicalNumber(string text, CultureInfo culture)
    {
        var decimalSeparator = culture.NumberFormat.NumberDecimalSeparator;
        var hasDecimal = false;
        var hasSign = false;
        var hasDigit = false;
        var characters = new List<char>(text.Length);

        foreach (var character in text)
        {
            if (!hasDigit && !hasSign && character == '-')
            {
                characters.Add(character);
                hasSign = true;
                continue;
            }

            if (char.IsDigit(character))
            {
                characters.Add(character);
                hasDigit = true;
                continue;
            }

            if (!hasDecimal && (character == '.' || decimalSeparator.Contains(character)))
            {
                characters.Add('.');
                hasDecimal = true;
            }
        }

        if (characters.Count == 1 && characters[0] == '-')
            return string.Empty;

        return new string(characters.ToArray());
    }

    public static string FormatCanonical(string canonical, CultureInfo culture)
    {
        var isNegative = canonical.StartsWith("-", StringComparison.Ordinal);
        var numberPart = isNegative ? canonical[1..] : canonical;

        if (numberPart.Length == 0)
            return string.Empty;

        var decimalIndex = numberPart.IndexOf('.');
        var hasDecimal = decimalIndex >= 0;

        var integerDigits = hasDecimal ? numberPart[..decimalIndex] : numberPart;
        var fractionalDigits = hasDecimal ? numberPart[(decimalIndex + 1)..] : string.Empty;

        if (integerDigits.Length == 0)
            integerDigits = "0";

        integerDigits = TrimLeadingZeros(integerDigits);
        var groupedInteger = GroupIntegerDigits(integerDigits, culture.NumberFormat.NumberGroupSeparator);

        var baseText = !hasDecimal
            ? groupedInteger
            : fractionalDigits.Length == 0
                ? groupedInteger + culture.NumberFormat.NumberDecimalSeparator
                : groupedInteger + culture.NumberFormat.NumberDecimalSeparator + fractionalDigits;

        return isNegative && baseText != "0" ? "-" + baseText : baseText;
    }

    private static string FormatAbbreviated(decimal scaledAmount, string suffix, CultureInfo culture)
    {
        var rounded = decimal.Round(scaledAmount, 1, MidpointRounding.AwayFromZero);
        var formatted = rounded.ToString("0.#", culture);

        if (formatted == "-0")
            formatted = "0";

        return formatted + suffix;
    }

    private static bool TryCreateCanonical(object? value, CultureInfo culture, out string canonical)
    {
        canonical = string.Empty;
        if (value is null)
            return false;

        if (value is string text)
        {
            canonical = BuildCanonicalNumber(text, culture);
            return canonical.Length > 0;
        }

        if (!TryConvertToDecimal(value, out var amount))
            return false;

        canonical = amount.ToString("0.############################", CultureInfo.InvariantCulture);
        return canonical.Length > 0;
    }

    private static bool TryConvertToDecimal(object value, out decimal amount)
    {
        switch (value)
        {
            case decimal decimalValue:
                amount = decimalValue;
                return true;
            case double doubleValue:
                amount = Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);
                return true;
            case float floatValue:
                amount = Convert.ToDecimal(floatValue, CultureInfo.InvariantCulture);
                return true;
            case int intValue:
                amount = intValue;
                return true;
            case long longValue:
                amount = longValue;
                return true;
            case short shortValue:
                amount = shortValue;
                return true;
            case byte byteValue:
                amount = byteValue;
                return true;
            default:
                if (value is IConvertible convertible)
                {
                    try
                    {
                        amount = convertible.ToDecimal(CultureInfo.InvariantCulture);
                        return true;
                    }
                    catch (FormatException)
                    {
                    }
                    catch (InvalidCastException)
                    {
                    }
                    catch (OverflowException)
                    {
                    }
                }

                amount = 0m;
                return false;
        }
    }

    private static bool TryParseCanonical(string canonical, out decimal amount)
    {
        return decimal.TryParse(
            canonical,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out amount);
    }

    private static int GetIntegerDigitCount(string canonical)
    {
        var isNegative = canonical.StartsWith("-", StringComparison.Ordinal);
        var numberPart = isNegative ? canonical[1..] : canonical;
        if (numberPart.Length == 0)
            return 0;

        var decimalIndex = numberPart.IndexOf('.');
        var integerDigits = decimalIndex >= 0 ? numberPart[..decimalIndex] : numberPart;
        if (integerDigits.Length == 0)
            return 1;

        var trimmed = TrimLeadingZeros(integerDigits);
        return trimmed.Length == 0 ? 1 : trimmed.Length;
    }

    private static string TrimLeadingZeros(string digits)
    {
        var index = 0;
        while (index < digits.Length - 1 && digits[index] == '0')
            index++;

        return digits[index..];
    }

    private static string GroupIntegerDigits(string digits, string groupSeparator)
    {
        if (digits.Length <= 3)
            return digits;

        var builder = new StringBuilder(digits.Length + (digits.Length / 3));
        var headLength = digits.Length % 3;
        if (headLength == 0)
            headLength = 3;

        builder.Append(digits[..headLength]);

        for (var i = headLength; i < digits.Length; i += 3)
        {
            builder.Append(groupSeparator);
            builder.Append(digits, i, 3);
        }

        return builder.ToString();
    }
}
