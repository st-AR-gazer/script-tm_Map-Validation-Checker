namespace MapValidationChecker.Cli.Infrastructure;

internal static class GbxTime
{
    public static int? ToMilliseconds(object? timeValue)
    {
        if (timeValue is null)
            return null;

        var type = timeValue.GetType();

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            var hasValue = (bool)(type.GetProperty("HasValue")?.GetValue(timeValue) ?? false);
            if (!hasValue)
                return null;

            timeValue = type.GetProperty("Value")?.GetValue(timeValue);
            if (timeValue is null)
                return null;

            type = timeValue.GetType();
        }

        if (timeValue is int intValue)
            return intValue;
        if (timeValue is long longValue)
            return checked((int)longValue);
        if (timeValue is uint uintValue)
            return unchecked((int)uintValue);
        if (timeValue is TimeSpan timeSpan)
            return (int)Math.Round(timeSpan.TotalMilliseconds);

        object? candidate =
            type.GetProperty("TotalMilliseconds")?.GetValue(timeValue) ??
            type.GetProperty("Milliseconds")?.GetValue(timeValue) ??
            type.GetProperty("Value")?.GetValue(timeValue);

        if (candidate is not null)
        {
            if (!ReferenceEquals(candidate, timeValue))
            {
                var nestedValue = ToMilliseconds(candidate);
                if (nestedValue.HasValue)
                    return nestedValue.Value;
            }

            if (candidate is double doubleValue)
                return (int)Math.Round(doubleValue);
            if (candidate is float floatValue)
                return (int)Math.Round(floatValue);
            if (int.TryParse(candidate.ToString(), out var parsedCandidate))
                return parsedCandidate;
        }

        return TryParse(timeValue.ToString(), out var parsedTime)
            ? parsedTime
            : null;
    }

    public static bool TryParse(string? value, out int milliseconds)
    {
        milliseconds = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim();

        var parts = value.Split(
            ':',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        int hours = 0;
        int minutes = 0;
        string secondsPart;

        if (parts.Length == 3)
        {
            if (!int.TryParse(parts[0], out hours))
                return false;
            if (!int.TryParse(parts[1], out minutes))
                return false;
            secondsPart = parts[2];
        }
        else if (parts.Length == 2)
        {
            if (!int.TryParse(parts[0], out minutes))
                return false;
            secondsPart = parts[1];
        }
        else if (parts.Length == 1)
        {
            secondsPart = parts[0];
        }
        else
        {
            return false;
        }

        var secondsAndMilliseconds = secondsPart.Split('.', StringSplitOptions.TrimEntries);
        if (!int.TryParse(secondsAndMilliseconds[0], out var seconds))
            return false;

        int millisecondsPart = 0;
        if (secondsAndMilliseconds.Length > 1)
        {
            var millisecondsText = secondsAndMilliseconds[1];
            if (millisecondsText.Length > 3)
                millisecondsText = millisecondsText[..3];
            if (millisecondsText.Length < 3)
                millisecondsText = millisecondsText.PadRight(3, '0');
            if (!int.TryParse(millisecondsText, out millisecondsPart))
                return false;
        }

        var total =
            (long)hours * 3_600_000L +
            (long)minutes * 60_000L +
            (long)seconds * 1_000L +
            millisecondsPart;
        if (total < 0 || total > int.MaxValue)
            return false;

        milliseconds = (int)total;
        return true;
    }
}
