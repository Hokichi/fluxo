using System.Globalization;
using System.Windows.Data;

namespace Fluxo.Resources.Converters;

public class MoneyAmountToCanonicalConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var options = ConverterOptions.FromParameter(parameter);
        var sourceCulture = culture ?? CultureInfo.CurrentCulture;
        var text = value as string ?? System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        var canonical = BuildCanonicalNumber(text, sourceCulture, options.AllowDecimal);
        if (canonical.Length == 0)
            return string.Empty;

        var groupSeparator = options.UseCommaAsGroupSeparator
            ? ","
            : sourceCulture.NumberFormat.NumberGroupSeparator;

        return FormatCanonical(canonical, options.AllowDecimal, groupSeparator, sourceCulture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var options = ConverterOptions.FromParameter(parameter);
        var sourceCulture = culture ?? CultureInfo.CurrentCulture;
        var text = value as string ?? System.Convert.ToString(value, sourceCulture) ?? string.Empty;
        var canonical = BuildCanonicalNumber(text, sourceCulture, options.AllowDecimal);
        if (canonical.Length == 0)
            return Binding.DoNothing;

        return decimal.TryParse(
            canonical,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var amount)
            ? amount
            : Binding.DoNothing;
    }

    private static string BuildCanonicalNumber(string text, CultureInfo culture, bool allowDecimal)
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

            if (!allowDecimal)
                continue;

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

    private static string FormatCanonical(string canonical, bool allowDecimal, string groupSeparator, CultureInfo culture)
    {
        if (canonical.Length == 0)
            return string.Empty;

        var isNegative = canonical.StartsWith("-", StringComparison.Ordinal);
        var numberPart = isNegative ? canonical[1..] : canonical;
        if (numberPart.Length == 0)
            return string.Empty;

        var decimalIndex = numberPart.IndexOf('.');
        var hasDecimal = allowDecimal && decimalIndex >= 0;

        var integerDigits = hasDecimal ? numberPart[..decimalIndex] : numberPart;
        var fractionalDigits = hasDecimal ? numberPart[(decimalIndex + 1)..] : string.Empty;

        if (integerDigits.Length == 0)
            integerDigits = "0";

        integerDigits = TrimLeadingZeros(integerDigits);
        var groupedInteger = GroupIntegerDigits(integerDigits, groupSeparator);
        if (!hasDecimal)
            return isNegative && groupedInteger != "0" ? "-" + groupedInteger : groupedInteger;

        var decimalSeparator = culture.NumberFormat.NumberDecimalSeparator;
        var baseText = fractionalDigits.Length == 0
            ? groupedInteger + decimalSeparator
            : groupedInteger + decimalSeparator + fractionalDigits;

        return baseText;
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

        var builder = new System.Text.StringBuilder(digits.Length + (digits.Length / 3));

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

    private readonly record struct ConverterOptions(bool AllowDecimal, bool UseCommaAsGroupSeparator)
    {
        public static ConverterOptions FromParameter(object parameter)
        {
            var allowDecimal = true;
            var useCommaAsGroupSeparator = false;

            if (parameter is not string text || string.IsNullOrWhiteSpace(text))
                return new ConverterOptions(allowDecimal, useCommaAsGroupSeparator);

            foreach (var token in text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (token.Equals("NoDecimal", StringComparison.OrdinalIgnoreCase))
                {
                    allowDecimal = false;
                    continue;
                }

                if (token.Equals("Comma", StringComparison.OrdinalIgnoreCase))
                {
                    useCommaAsGroupSeparator = true;
                    continue;
                }

                if (!token.Contains('=', StringComparison.Ordinal))
                    continue;

                var pair = token.Split('=', 2, StringSplitOptions.TrimEntries);
                if (pair.Length != 2)
                    continue;

                if (pair[0].Equals("AllowDecimal", StringComparison.OrdinalIgnoreCase))
                {
                    if (bool.TryParse(pair[1], out var parsedAllowDecimal))
                        allowDecimal = parsedAllowDecimal;
                }
                else if (pair[0].Equals("UseCommaAsGroupSeparator", StringComparison.OrdinalIgnoreCase))
                {
                    if (bool.TryParse(pair[1], out var parsedUseComma))
                        useCommaAsGroupSeparator = parsedUseComma;
                }
            }

            return new ConverterOptions(allowDecimal, useCommaAsGroupSeparator);
        }
    }
}
