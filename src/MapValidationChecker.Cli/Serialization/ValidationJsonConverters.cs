using System.Text.Json;
using System.Text.Json.Serialization;

using MapValidationChecker.Core.Validation;

namespace MapValidationChecker.Cli.Serialization;

internal static class ValidationJsonConverters
{
    public static void AddTo(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Converters.Add(new ValidationStatusJsonConverter());
        options.Converters.Add(new ValidationTypeJsonConverter());
    }

    private sealed class ValidationStatusJsonConverter : JsonConverter<ValidationStatus>
    {
        public override ValidationStatus Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            return reader.GetString() switch
            {
                "Yes" => ValidationStatus.Yes,
                "Maybe" => ValidationStatus.Maybe,
                "Unknown" => ValidationStatus.Unknown,
                _ => throw new JsonException("Unknown validation status.")
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            ValidationStatus value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value switch
            {
                ValidationStatus.Yes => "Yes",
                ValidationStatus.Maybe => "Maybe",
                ValidationStatus.Unknown => "Unknown",
                _ => throw new JsonException("Unknown validation status.")
            });
        }
    }

    private sealed class ValidationTypeJsonConverter : JsonConverter<ValidationType>
    {
        public override ValidationType Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            return reader.GetString() switch
            {
                "normal" => ValidationType.Normal,
                "plugin" => ValidationType.Plugin,
                "validationghost" => ValidationType.ValidationGhost,
                "validationtag" => ValidationType.ValidationTag,
                "gps" => ValidationType.Gps,
                "replay" => ValidationType.Replay,
                "manual" => ValidationType.Manual,
                _ => throw new JsonException("Unknown validation type.")
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            ValidationType value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value switch
            {
                ValidationType.Normal => "normal",
                ValidationType.Plugin => "plugin",
                ValidationType.ValidationGhost => "validationghost",
                ValidationType.ValidationTag => "validationtag",
                ValidationType.Gps => "gps",
                ValidationType.Replay => "replay",
                ValidationType.Manual => "manual",
                _ => throw new JsonException("Unknown validation type.")
            });
        }
    }
}
