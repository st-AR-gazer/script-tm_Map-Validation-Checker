using System.Text.Json;

using MapValidationChecker.Cli.Serialization;
using MapValidationChecker.Core.Validation;

namespace MapValidationChecker.CharacterizationTests;

public sealed class ValidationJsonContractTests
{
    [Theory]
    [InlineData(ValidationStatus.Yes, "Yes")]
    [InlineData(ValidationStatus.Maybe, "Maybe")]
    [InlineData(ValidationStatus.Unknown, "Unknown")]
    public void Validation_status_round_trips_through_its_established_wire_value(
        ValidationStatus value,
        string wireValue)
    {
        var options = CreateOptions();
        var json = JsonSerializer.Serialize(value, options);

        Assert.Equal($"\"{wireValue}\"", json);
        Assert.Equal(value, JsonSerializer.Deserialize<ValidationStatus>(json, options));
    }

    [Theory]
    [InlineData(ValidationType.Normal, "normal")]
    [InlineData(ValidationType.Plugin, "plugin")]
    [InlineData(ValidationType.ValidationGhost, "validationghost")]
    [InlineData(ValidationType.ValidationTag, "validationtag")]
    [InlineData(ValidationType.Gps, "gps")]
    [InlineData(ValidationType.Replay, "replay")]
    [InlineData(ValidationType.Manual, "manual")]
    public void Validation_type_round_trips_through_its_established_wire_value(
        ValidationType value,
        string wireValue)
    {
        var options = CreateOptions();
        var json = JsonSerializer.Serialize(value, options);

        Assert.Equal($"\"{wireValue}\"", json);
        Assert.Equal(value, JsonSerializer.Deserialize<ValidationType>(json, options));
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        ValidationJsonConverters.AddTo(options);
        return options;
    }
}
