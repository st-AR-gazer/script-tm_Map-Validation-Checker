using System.Text.Json;

namespace MapValidationChecker.Cli.Serialization;

internal static class ValidationReportJson
{
    private static readonly JsonSerializerOptions CompactOptions = CreateOptions(writeIndented: false);
    private static readonly JsonSerializerOptions PrettyOptions = CreateOptions(writeIndented: true);

    public static string Serialize(object value, bool pretty)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.Serialize(value, pretty ? PrettyOptions : CompactOptions);
    }

    private static JsonSerializerOptions CreateOptions(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = writeIndented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        ValidationJsonConverters.AddTo(options);
        return options;
    }
}
