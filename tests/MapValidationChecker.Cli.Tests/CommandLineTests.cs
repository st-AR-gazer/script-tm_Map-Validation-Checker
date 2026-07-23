using MapValidationChecker.Cli.CommandLine;
using MapValidationChecker.Cli.Infrastructure;

namespace MapValidationChecker.Cli.Tests;

public sealed class CommandLineTests
{
    [Fact]
    public void Single_mode_defaults_are_explicit()
    {
        using var directory = new TemporaryDirectory();
        var mapPath = directory.GetPath("map.Map.Gbx");
        File.WriteAllBytes(mapPath, [0x47, 0x42, 0x58]);

        var options = CliArguments.Parse(["--single", mapPath]);

        Assert.Equal(RunMode.Single, options.Mode);
        Assert.Equal(mapPath, options.MapPath);
        Assert.True(options.GpsEnabled);
        Assert.False(options.StrictGps);
        Assert.Equal(100, options.GpsThresholdMs);
        Assert.True(options.IncludeMapName);
        Assert.False(options.IncludePath);
        Assert.Equal(5, options.ProgressIntervalSeconds);
        Assert.Null(options.MaxDepth);
    }

    [Theory]
    [InlineData("--strict-gps", "--no-gps", false, false)]
    [InlineData("--no-gps", "--strict-gps", true, true)]
    public void Last_gps_mode_option_wins(
        string firstOption,
        string secondOption,
        bool expectedGpsEnabled,
        bool expectedStrictGps)
    {
        using var directory = new TemporaryDirectory();
        var mapPath = directory.GetPath("map.Map.Gbx");
        File.WriteAllBytes(mapPath, [0x47, 0x42, 0x58]);

        var options = CliArguments.Parse(
            ["--single", mapPath, firstOption, secondOption]);

        Assert.Equal(expectedGpsEnabled, options.GpsEnabled);
        Assert.Equal(expectedStrictGps, options.StrictGps);
    }

    [Fact]
    public void Help_is_detected_before_path_validation()
    {
        Assert.True(CliArguments.IsHelpRequested(["--single", "missing.Map.Gbx", "--help"]));
        Assert.False(CliArguments.IsHelpRequested([]));
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(10, 0, 0)]
    [InlineData(10, 2, 5)]
    public void Progress_rate_handles_edges(int processed, double seconds, double expected)
    {
        Assert.Equal(expected, ProgressReporter.GetRate(processed, seconds));
    }

    [Theory]
    [InlineData(0, 10, "0m00s")]
    [InlineData(10, 0, "unknown")]
    [InlineData(125, 1, "2m05s")]
    public void Progress_eta_is_stably_formatted(int remaining, double rate, string expected)
    {
        Assert.Equal(expected, ProgressReporter.GetEta(remaining, rate));
    }
}
