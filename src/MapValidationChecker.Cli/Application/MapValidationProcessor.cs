using GBX.NET;
using GBX.NET.Engines.Game;

using MapValidationChecker.Cli.Diagnostics;
using MapValidationChecker.Cli.Evidence;
using MapValidationChecker.Cli.Infrastructure;
using MapValidationChecker.Cli.Serialization;
using MapValidationChecker.Core.Validation;

namespace MapValidationChecker.Cli.Application;

internal sealed class MapValidationProcessor
{
    private readonly ValidationEngine validator = new();

    public ValidationReport Process(
        string mapFilePath,
        MapProcessingOptions options,
        ManualOverrideCatalog manualOverrides,
        ReplayEvidenceIndex replayIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapFilePath);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(manualOverrides);
        ArgumentNullException.ThrowIfNull(replayIndex);

        var report = new ValidationReport();

        if (options.IncludePath)
            report.Path = mapFilePath;

        if (!GbxFile.HasMagic(mapFilePath))
        {
            report.Error = "not a gbx file";
            return report;
        }

        CGameCtnChallenge map;
        try
        {
            map = Gbx.ParseNode<CGameCtnChallenge>(mapFilePath);
        }
        catch (Exception exception)
        {
            report.Error = "failed to parse map gbx";
            report.Note = $"{exception.GetType().Name}: {exception.Message}";
            return report;
        }

        report.Uid = map.MapUid;
        if (options.IncludeMapName)
            report.MapName = map.MapName;

        var nonGpsEvidence = NonGpsEvidenceReader.Read(map, manualOverrides, replayIndex);
        var authorTimeMs = nonGpsEvidence.AuthorTimeMs;

        if (options.DataDump)
            report.DataDump = MapDataDumpReader.Read(map, authorTimeMs, options.MaxDepth);

        var validationInput = new ValidationInput
        {
            AuthorTimeMs = authorTimeMs,
            ManualOverride = nonGpsEvidence.ManualOverride,
            ValidationGhostTimeMs = nonGpsEvidence.ValidationGhostTimeMs,
            ValidationTag = nonGpsEvidence.ValidationTag,
            MatchingReplay = nonGpsEvidence.MatchingReplay,
            WaypointMetadata = nonGpsEvidence.WaypointMetadata,
            Checkpoints = nonGpsEvidence.Checkpoints,
            Gps = options.GpsEnabled ? GpsEvaluation.NotEvaluated : GpsEvaluation.Disabled,
            StrictGps = options.StrictGps,
            GpsThresholdMs = options.GpsThresholdMs
        };

        var evaluation = validator.Evaluate(validationInput);
        if (evaluation.RequiresGpsEvidence)
        {
            var gpsEvidence = GpsEvidenceReader.FindMatch(
                map,
                authorTimeMs!.Value,
                options.MaxDepth,
                options.GpsThresholdMs);
            validationInput = validationInput with
            {
                Gps = gpsEvidence is null
                    ? GpsEvaluation.NoMatch
                    : GpsEvaluation.Matched(gpsEvidence)
            };
            evaluation = validator.Evaluate(validationInput);
        }

        var outcome = evaluation.Outcome ??
            throw new InvalidOperationException(
                "Validation engine did not produce a terminal outcome.");

        report.Validated = outcome.Status;
        report.Type = outcome.Type;
        report.Note = outcome.Note;
        report.Error = outcome.Error;

        if (options.IncludePath && !string.IsNullOrWhiteSpace(outcome.ReplayPath))
            report.ReplayPath = outcome.ReplayPath;

        if (outcome.GpsEvidence is not null && authorTimeMs.HasValue)
        {
            report.GpsValidation = GpsValidationDetails.FromEvidence(
                authorTimeMs.Value,
                options.GpsThresholdMs,
                outcome.GpsEvidence);
        }

        return report;
    }
}
