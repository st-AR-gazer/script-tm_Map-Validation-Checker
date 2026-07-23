using MapValidationChecker.Cli.Diagnostics;
using MapValidationChecker.Core.Validation;

namespace MapValidationChecker.Cli.Serialization;

internal sealed class ValidationReport
{
    public string? Uid { get; set; }
    public ValidationStatus? Validated { get; set; }
    public ValidationType? Type { get; set; }
    public string? Note { get; set; }
    public GpsValidationDetails? GpsValidation { get; set; }
    public string? Path { get; set; }
    public string? MapName { get; set; }
    public string? ReplayPath { get; set; }
    public string? Error { get; set; }
    public MapDataDump? DataDump { get; set; }
}
