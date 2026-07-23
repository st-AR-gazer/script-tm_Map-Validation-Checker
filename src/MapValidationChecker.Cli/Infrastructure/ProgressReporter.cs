using System.Diagnostics;

namespace MapValidationChecker.Cli.Infrastructure;

internal sealed class ProgressReporter
{
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly TimeSpan interval;
    private TimeSpan lastReportAt = TimeSpan.Zero;
    private double lastReportSeconds;
    private int lastReportCount;

    public ProgressReporter(TimeSpan interval)
    {
        this.interval = interval;
    }

    public bool TryGetStats(int currentCount, out ProgressStats stats)
    {
        var elapsed = stopwatch.Elapsed;
        if (elapsed - lastReportAt < interval)
        {
            stats = default;
            return false;
        }

        lastReportAt = elapsed;

        var elapsedSeconds = elapsed.TotalSeconds;
        var averageRate = GetRate(currentCount, elapsedSeconds);
        var intervalSeconds = elapsedSeconds - lastReportSeconds;
        var intervalCount = currentCount - lastReportCount;
        var intervalRate = GetRate(intervalCount, intervalSeconds);

        lastReportSeconds = elapsedSeconds;
        lastReportCount = currentCount;

        stats = new ProgressStats(
            FormatElapsed(elapsed),
            averageRate,
            intervalRate);
        return true;
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
        if (seconds < 0)
            seconds = 0;
        return FormatElapsed(TimeSpan.FromSeconds(seconds));
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        var minutes = (int)elapsed.TotalMinutes;
        return $"{minutes}m{elapsed.Seconds:D2}s";
    }
}

internal readonly record struct ProgressStats(
    string Elapsed,
    double AvgRate,
    double IntervalRate);
