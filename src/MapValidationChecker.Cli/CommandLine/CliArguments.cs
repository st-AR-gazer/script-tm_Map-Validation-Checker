namespace MapValidationChecker.Cli.CommandLine;

internal static class CliArguments
{
    private const int DefaultGpsThresholdMs = 100;

    public static bool IsHelpRequested(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Contains("--help");
    }

    public static CliOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
            throw new CliArgumentException("No arguments provided.");

        string? single = null;
        string? batch = null;
        string? replays = null;
        string? manual = null;
        var recursive = false;
        var pretty = false;
        var includePath = false;
        var includeMapName = true;
        var progress = false;
        double progressIntervalSeconds = 5;
        string? output = null;
        var gpsEnabled = true;
        var strictGps = false;
        var gpsThresholdMs = DefaultGpsThresholdMs;
        var dataDump = false;
        int? maxDepth = null;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            string Next()
            {
                if (index + 1 >= args.Length)
                    throw new CliArgumentException($"Missing value after {argument}");
                return args[++index];
            }

            switch (argument)
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
                    {
                        throw new CliArgumentException(
                            "--progress-interval must be a positive number (seconds)");
                    }
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
                    {
                        throw new CliArgumentException(
                            "--gps-threshold-ms must be a non-negative integer");
                    }
                    gpsThresholdMs = gpsThreshold;
                    break;

                case "--data-dump":
                    dataDump = true;
                    break;

                case "--max-depth":
                    if (!int.TryParse(Next(), out var depth) || depth < 0)
                    {
                        throw new CliArgumentException(
                            "--max-depth must be a non-negative integer");
                    }
                    maxDepth = depth;
                    break;

                case "--help":
                    break;

                default:
                    throw new CliArgumentException($"Unknown flag: {argument}");
            }
        }

        if ((single is null) == (batch is null))
        {
            throw new CliArgumentException(
                "You must specify exactly one of --single or --batch.");
        }

        var mode = single is not null ? RunMode.Single : RunMode.Batch;
        var mapPath = single ?? batch!;

        if (mode == RunMode.Single && !File.Exists(mapPath))
            throw new CliArgumentException($"Map file does not exist: {mapPath}");

        if (mode == RunMode.Batch && !Directory.Exists(mapPath))
            throw new CliArgumentException($"Map folder does not exist: {mapPath}");

        if (!string.IsNullOrWhiteSpace(replays) &&
            !File.Exists(replays) &&
            !Directory.Exists(replays))
        {
            throw new CliArgumentException($"Replay path does not exist: {replays}");
        }

        if (!string.IsNullOrWhiteSpace(manual) && !File.Exists(manual))
            throw new CliArgumentException($"Manual JSON file does not exist: {manual}");

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
            maxDepth);
    }
}
