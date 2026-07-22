using System.Diagnostics;
using System.Text.Json;

using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.LZO;
using GBX.NET.ZLib;

using MapValidationChecker.Cli.Diagnostics;
using MapValidationChecker.Cli.Evidence;
using MapValidationChecker.Cli.Infrastructure;
using MapValidationChecker.Cli.Serialization;
using MapValidationChecker.Core.Validation;

namespace MapValidationChecker.Cli;

internal sealed class Program
{
    private const int DefaultGpsThresholdMs = 100;
    private static readonly ValidationEngine Validator = new();

    private static int Main(string[] args)
    {
        try
        {
            var opts = ParseArgs(args);

            Gbx.LZO = new Lzo();
            Gbx.ZLib = new ZLib();

            var manual = !string.IsNullOrWhiteSpace(opts.ManualPath)
                ? ManualOverrideCatalog.Load(opts.ManualPath!)
                : ManualOverrideCatalog.Empty;

            var replayIndex = !string.IsNullOrWhiteSpace(opts.ReplaysPath)
                ? BuildReplayEvidenceIndex(
                    opts.ReplaysPath!,
                    opts.Recursive,
                    opts.Progress,
                    opts.ProgressIntervalSeconds)
                : ReplayEvidenceIndex.Empty;

            object outputObj;
            if (opts.Mode == RunMode.Single)
            {
                var report = ProcessMapFile(opts.MapPath, opts, manual, replayIndex);
                outputObj = report;
            }
            else
            {
                var mapFiles = EnumerateFiles(opts.MapPath, opts.Recursive).ToList();
                var totalMaps = mapFiles.Count;
                var progress = opts.Progress ? new ProgressReporter(TimeSpan.FromSeconds(opts.ProgressIntervalSeconds)) : null;
                int processed = 0;
                int errorCount = 0;

                var reports = new List<Report>();
                foreach (var file in mapFiles)
                {
                    var report = ProcessMapFile(file, opts, manual, replayIndex);
                    reports.Add(report);

                    processed++;
                    if (!string.IsNullOrWhiteSpace(report.Error))
                        errorCount++;

                    if (progress is not null && progress.TryGetStats(processed, out var mapStats))
                    {
                        var eta = ProgressReporter.GetEta(totalMaps - processed, mapStats.AvgRate);
                        Console.Error.WriteLine(
                            $"Map scan: {processed}/{totalMaps} files, errors={errorCount}, rate={mapStats.AvgRate:F1}/s (last {mapStats.IntervalRate:F1}/s), eta={eta}, elapsed={mapStats.Elapsed}");
                    }
                }
                outputObj = reports;
            }

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = opts.Pretty,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            ValidationJsonConverters.AddTo(jsonOptions);

            var json = JsonSerializer.Serialize(outputObj, jsonOptions);

            if (!string.IsNullOrWhiteSpace(opts.OutputPath))
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(opts.OutputPath!));
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(opts.OutputPath!, json);
            }

            Console.WriteLine(json);
            return 0;
        }
        catch (ArgException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            PrintHelp();
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Fatal error: " + ex);
            return 1;
        }
    }

    // ----------------------------
    // Core processing
    // ----------------------------

    private static Report ProcessMapFile(
        string mapFilePath,
        CliOptions opts,
        ManualOverrideCatalog manual,
        ReplayEvidenceIndex replayIndex)
    {
        var report = new Report();

        if (opts.IncludePath)
            report.Path = mapFilePath;

        if (!GbxFile.HasMagic(mapFilePath))
        {
            report.Error = "not a gbx file";
            return report;
        }

        CGameCtnChallenge? map = null;
        try
        {
            map = Gbx.ParseNode<CGameCtnChallenge>(mapFilePath);
        }
        catch (Exception ex)
        {
            report.Error = "failed to parse map gbx";
            report.Note = $"{ex.GetType().Name}: {ex.Message}";
            return report;
        }

        report.Uid = map.MapUid;
        if (opts.IncludeMapName)
            report.MapName = map.MapName;

        var nonGpsEvidence = NonGpsEvidenceReader.Read(map, manual, replayIndex);
        var authorMs = nonGpsEvidence.AuthorTimeMs;

        if (opts.DataDump)
            report.DataDump = MapDataDumpReader.Read(map, authorMs, opts.MaxDepth);

        var validationInput = new ValidationInput
        {
            AuthorTimeMs = authorMs,
            ManualOverride = nonGpsEvidence.ManualOverride,
            ValidationGhostTimeMs = nonGpsEvidence.ValidationGhostTimeMs,
            ValidationTag = nonGpsEvidence.ValidationTag,
            MatchingReplay = nonGpsEvidence.MatchingReplay,
            WaypointMetadata = nonGpsEvidence.WaypointMetadata,
            Checkpoints = nonGpsEvidence.Checkpoints,
            Gps = opts.GpsEnabled ? GpsEvaluation.NotEvaluated : GpsEvaluation.Disabled,
            StrictGps = opts.StrictGps,
            GpsThresholdMs = opts.GpsThresholdMs
        };

        var evaluation = Validator.Evaluate(validationInput);
        if (evaluation.RequiresGpsEvidence)
        {
            var gpsEvidence = GpsEvidenceReader.FindMatch(
                map,
                authorMs!.Value,
                opts.MaxDepth,
                opts.GpsThresholdMs);
            var gpsEvaluation = gpsEvidence is not null
                    ? GpsEvaluation.Matched(gpsEvidence)
                    : GpsEvaluation.NoMatch;

            validationInput = validationInput with { Gps = gpsEvaluation };
            evaluation = Validator.Evaluate(validationInput);
        }

        var outcome = evaluation.Outcome ??
            throw new InvalidOperationException("Validation engine did not produce a terminal outcome.");

        report.Validated = outcome.Status;
        report.Type = outcome.Type;
        report.Note = outcome.Note;
        report.Error = outcome.Error;

        if (opts.IncludePath && !string.IsNullOrWhiteSpace(outcome.ReplayPath))
            report.ReplayPath = outcome.ReplayPath;

        if (outcome.GpsEvidence is not null && authorMs.HasValue)
        {
            report.GpsValidation = GpsValidationDetails.FromEvidence(
                authorMs.Value,
                opts.GpsThresholdMs,
                outcome.GpsEvidence);
        }

        return report;
    }

    // ----------------------------
    // Replay evidence orchestration
    // ----------------------------

    private static ReplayEvidenceIndex BuildReplayEvidenceIndex(
        string path,
        bool recursive,
        bool progressEnabled,
        double progressIntervalSeconds)
    {
        var files = EnumerateFiles(path, recursive).ToList();
        var progress = progressEnabled
            ? new ProgressReporter(TimeSpan.FromSeconds(progressIntervalSeconds))
            : null;

        return ReplayEvidenceIndex.Build(files, scanProgress =>
        {
            if (progress is null || !progress.TryGetStats(scanProgress.Scanned, out var stats))
                return;

            var eta = ProgressReporter.GetEta(scanProgress.Total - scanProgress.Scanned, stats.AvgRate);
            Console.Error.WriteLine(
                $"Replay scan: {scanProgress.Scanned}/{scanProgress.Total} files, gbx={scanProgress.GbxCount}, indexed={scanProgress.IndexedCount}, rate={stats.AvgRate:F1}/s (last {stats.IntervalRate:F1}/s), eta={eta}, elapsed={stats.Elapsed}");
        });
    }

    // ----------------------------
    // File helpers
    // ----------------------------

    private static IEnumerable<string> EnumerateFiles(string path, bool recursive)
    {
        if (File.Exists(path))
        {
            yield return path;
            yield break;
        }

        if (!Directory.Exists(path))
            yield break;

        var opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var file in Directory.EnumerateFiles(path, "*", opt))
            yield return file;
    }

    // ----------------------------
    // CLI parsing
    // ----------------------------

    private static CliOptions ParseArgs(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help"))
            throw new ArgException("No arguments provided.");

        string? single = null;
        string? batch = null;

        string? replays = null;
        string? manual = null;

        bool recursive = false;
        bool pretty = false;
        bool includePath = false;
        bool includeMapName = true;
        bool progress = false;
        double progressIntervalSeconds = 5;

        string? output = null;

        bool gpsEnabled = true;
        bool strictGps = false;
        int gpsThresholdMs = DefaultGpsThresholdMs;
        bool dataDump = false;

        int? maxDepth = null;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];

            string Next()
            {
                if (i + 1 >= args.Length)
                    throw new ArgException($"Missing value after {a}");
                return args[++i];
            }

            switch (a)
            {
                case "--single":
                    single = Next();
                    break;

                case "--batch":
                    batch = Next();
                    break;

                case "--replays":
                    replays = Next();
                    break;

                case "--manual":
                    manual = Next();
                    break;

                case "--recursive":
                    recursive = true;
                    break;

                case "--pretty":
                    pretty = true;
                    break;

                case "--include-path":
                    includePath = true;
                    break;

                case "--no-map-name":
                    includeMapName = false;
                    break;

                case "--progress":
                    progress = true;
                    break;

                case "--progress-interval":
                    if (!double.TryParse(Next(), out var interval) || interval <= 0)
                        throw new ArgException("--progress-interval must be a positive number (seconds)");
                    progressIntervalSeconds = interval;
                    break;

                case "--output":
                    output = Next();
                    break;

                case "--strict-gps":
                    strictGps = true;
                    gpsEnabled = true;
                    break;

                case "--no-gps":
                    gpsEnabled = false;
                    strictGps = false;
                    break;

                case "--gps-threshold-ms":
                    if (!int.TryParse(Next(), out var gpsThreshold) || gpsThreshold < 0)
                        throw new ArgException("--gps-threshold-ms must be a non-negative integer");
                    gpsThresholdMs = gpsThreshold;
                    break;

                case "--data-dump":
                    dataDump = true;
                    break;

                case "--max-depth":
                    if (!int.TryParse(Next(), out var d) || d < 0)
                        throw new ArgException("--max-depth must be a non-negative integer");
                    maxDepth = d;
                    break;

                case "--help":
                    break;

                default:
                    throw new ArgException($"Unknown flag: {a}");
            }
        }

        if ((single is null) == (batch is null))
            throw new ArgException("You must specify exactly one of --single or --batch.");

        var mode = single is not null ? RunMode.Single : RunMode.Batch;
        var mapPath = single ?? batch!;

        if (mode == RunMode.Single && !File.Exists(mapPath))
            throw new ArgException($"Map file does not exist: {mapPath}");

        if (mode == RunMode.Batch && !Directory.Exists(mapPath))
            throw new ArgException($"Map folder does not exist: {mapPath}");

        if (!string.IsNullOrWhiteSpace(replays))
        {
            if (!File.Exists(replays) && !Directory.Exists(replays))
                throw new ArgException($"Replay path does not exist: {replays}");
        }

        if (!string.IsNullOrWhiteSpace(manual))
        {
            if (!File.Exists(manual))
                throw new ArgException($"Manual JSON file does not exist: {manual}");
        }

        return new CliOptions(
            mode,
            mapPath,
            replays,
            manual,
            recursive,
            pretty,
            includePath,
            includeMapName,
            progress,
            progressIntervalSeconds,
            output,
            gpsEnabled,
            strictGps,
            gpsThresholdMs,
            dataDump,
            maxDepth
        );
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
@"Usage:
  MapValidationChecker --single <mapFile> [--replays <replayFileOrFolder>] [flags...]
  MapValidationChecker --batch  <mapFolder> [--replays <replayFolder>] [flags...]

Flags:
  --recursive                Recurse into subfolders (batch + replay scanning)
  --pretty                   Pretty-print JSON
  --include-path             Include ""path"" and ""replayPath"" (if matched)
  --no-map-name              Omit ""mapName"" from JSON output
  --progress                 Print periodic scan progress to stderr
  --progress-interval <sec>  Progress update interval in seconds (default: 5)
  --output <file>            Write JSON output to a file (also prints to stdout)
  --manual <file>            Manual overrides JSON (object or array of objects):
                             { ""valid"": true/false, ""uid"": ""..."", ""note"": ""..."" }

  --strict-gps               If GPS ghost matches author time => validated ""Yes"" (default: ""Maybe"")
  --no-gps                   Disable GPS scan
  --gps-threshold-ms <ms>    GPS author time tolerance in milliseconds (default: 100)
  --data-dump                Include raw parsed internals in output (U03, Samples2, metadata keys, etc.)
  --max-depth <n>            Limit reflection traversal depth for GPS scan (default: unlimited)

Notes:
  - Manual override has highest priority.
  - GPS times are stored to the nearest tenth of a second, so small discrepancies are expected.
  - If a validation ghost exists and its race time != author time, an error is returned."
        );
    }

    // ----------------------------
    // Models
    // ----------------------------

    private enum RunMode { Single, Batch }

    private sealed record CliOptions(
        RunMode Mode,
        string MapPath,
        string? ReplaysPath,
        string? ManualPath,
        bool Recursive,
        bool Pretty,
        bool IncludePath,
        bool IncludeMapName,
        bool Progress,
        double ProgressIntervalSeconds,
        string? OutputPath,
        bool GpsEnabled,
        bool StrictGps,
        int GpsThresholdMs,
        bool DataDump,
        int? MaxDepth
    );

    private sealed class Report
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

    private sealed class ArgException : Exception
    {
        public ArgException(string message) : base(message) { }
    }

    private sealed class ProgressReporter
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly TimeSpan _interval;
        private TimeSpan _last = TimeSpan.Zero;
        private double _lastReportSeconds;
        private int _lastReportCount;

        public ProgressReporter(TimeSpan interval)
        {
            _interval = interval;
        }

        public bool TryGetStats(int currentCount, out ProgressStats stats)
        {
            var elapsed = _sw.Elapsed;
            if (elapsed - _last < _interval)
            {
                stats = default;
                return false;
            }

            _last = elapsed;

            var elapsedSeconds = elapsed.TotalSeconds;
            var avgRate = GetRate(currentCount, elapsedSeconds);
            var intervalSeconds = elapsedSeconds - _lastReportSeconds;
            var intervalCount = currentCount - _lastReportCount;
            var intervalRate = GetRate(intervalCount, intervalSeconds);

            _lastReportSeconds = elapsedSeconds;
            _lastReportCount = currentCount;

            stats = new ProgressStats(FormatElapsed(elapsed), elapsedSeconds, avgRate, intervalRate);
            return true;
        }

        private static string FormatElapsed(TimeSpan t)
        {
            var minutes = (int)t.TotalMinutes;
            return $"{minutes}m{t.Seconds:D2}s";
        }

        public static double GetRate(int processed, double elapsedSeconds)
        {
            if (processed <= 0 || elapsedSeconds <= 0)
                return 0;
            return processed / elapsedSeconds;
        }

        public static string GetEta(int remaining, double rate)
        {
            if (remaining <= 0)
                return "0m00s";
            if (rate <= 0)
                return "unknown";

            var seconds = (int)Math.Round(remaining / rate);
            if (seconds < 0) seconds = 0;
            return FormatElapsed(TimeSpan.FromSeconds(seconds));
        }
    }

    private readonly record struct ProgressStats(
        string Elapsed,
        double ElapsedSeconds,
        double AvgRate,
        double IntervalRate
    );
}
