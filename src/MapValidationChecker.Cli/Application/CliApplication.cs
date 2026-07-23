using GBX.NET;
using GBX.NET.LZO;
using GBX.NET.ZLib;

using MapValidationChecker.Cli.CommandLine;
using MapValidationChecker.Cli.Evidence;
using MapValidationChecker.Cli.Infrastructure;
using MapValidationChecker.Cli.Serialization;

namespace MapValidationChecker.Cli.Application;

internal sealed class CliApplication
{
    private readonly TextWriter output;
    private readonly TextWriter error;
    private readonly MapValidationProcessor mapProcessor = new();

    public CliApplication(TextWriter output, TextWriter error)
    {
        this.output = output ?? throw new ArgumentNullException(nameof(output));
        this.error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public int Run(string[] args)
    {
        try
        {
            if (CliArguments.IsHelpRequested(args))
            {
                CliUsage.WriteTo(output);
                return 0;
            }

            var options = CliArguments.Parse(args);
            return Run(options);
        }
        catch (CliArgumentException exception)
        {
            error.WriteLine(exception.Message);
            error.WriteLine();
            CliUsage.WriteTo(output);
            return 2;
        }
        catch (Exception exception)
        {
            error.WriteLine("Fatal error: " + exception);
            return 1;
        }
    }

    private int Run(CliOptions options)
    {
        ConfigureGbxRuntime();

        var manualOverrides = !string.IsNullOrWhiteSpace(options.ManualPath)
            ? ManualOverrideCatalog.Load(options.ManualPath)
            : ManualOverrideCatalog.Empty;
        var replayIndex = !string.IsNullOrWhiteSpace(options.ReplaysPath)
            ? BuildReplayEvidenceIndex(options)
            : ReplayEvidenceIndex.Empty;
        var processingOptions = CreateMapProcessingOptions(options);

        object result = options.Mode switch
        {
            RunMode.Single => mapProcessor.Process(
                options.MapPath,
                processingOptions,
                manualOverrides,
                replayIndex),
            RunMode.Batch => ProcessBatch(
                options,
                processingOptions,
                manualOverrides,
                replayIndex),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Mode), "Unknown run mode.")
        };

        var json = ValidationReportJson.Serialize(result, options.Pretty);
        WriteOutputFile(options.OutputPath, json);
        output.WriteLine(json);
        return 0;
    }

    private List<ValidationReport> ProcessBatch(
        CliOptions options,
        MapProcessingOptions processingOptions,
        ManualOverrideCatalog manualOverrides,
        ReplayEvidenceIndex replayIndex)
    {
        var mapFiles = FileDiscovery.Enumerate(options.MapPath, options.Recursive).ToList();
        var totalMaps = mapFiles.Count;
        var progress = options.Progress
            ? new ProgressReporter(TimeSpan.FromSeconds(options.ProgressIntervalSeconds))
            : null;
        var processed = 0;
        var errorCount = 0;
        var reports = new List<ValidationReport>();

        foreach (var file in mapFiles)
        {
            var report = mapProcessor.Process(
                file,
                processingOptions,
                manualOverrides,
                replayIndex);
            reports.Add(report);

            processed++;
            if (!string.IsNullOrWhiteSpace(report.Error))
                errorCount++;

            if (progress is not null && progress.TryGetStats(processed, out var stats))
            {
                var eta = ProgressReporter.GetEta(totalMaps - processed, stats.AvgRate);
                error.WriteLine(
                    $"Map scan: {processed}/{totalMaps} files, errors={errorCount}, rate={stats.AvgRate:F1}/s (last {stats.IntervalRate:F1}/s), eta={eta}, elapsed={stats.Elapsed}");
            }
        }

        return reports;
    }

    private ReplayEvidenceIndex BuildReplayEvidenceIndex(CliOptions options)
    {
        var files = FileDiscovery
            .Enumerate(options.ReplaysPath!, options.Recursive)
            .ToList();
        var progress = options.Progress
            ? new ProgressReporter(TimeSpan.FromSeconds(options.ProgressIntervalSeconds))
            : null;

        return ReplayEvidenceIndex.Build(files, scanProgress =>
        {
            if (progress is null || !progress.TryGetStats(scanProgress.Scanned, out var stats))
                return;

            var eta = ProgressReporter.GetEta(
                scanProgress.Total - scanProgress.Scanned,
                stats.AvgRate);
            error.WriteLine(
                $"Replay scan: {scanProgress.Scanned}/{scanProgress.Total} files, gbx={scanProgress.GbxCount}, indexed={scanProgress.IndexedCount}, rate={stats.AvgRate:F1}/s (last {stats.IntervalRate:F1}/s), eta={eta}, elapsed={stats.Elapsed}");
        });
    }

    private static MapProcessingOptions CreateMapProcessingOptions(CliOptions options) =>
        new(
            options.IncludePath,
            options.IncludeMapName,
            options.GpsEnabled,
            options.StrictGps,
            options.GpsThresholdMs,
            options.DataDump,
            options.MaxDepth);

    private static void ConfigureGbxRuntime()
    {
        Gbx.LZO = new Lzo();
        Gbx.ZLib = new ZLib();
    }

    private static void WriteOutputFile(string? outputPath, string json)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(outputPath, json);
    }
}
