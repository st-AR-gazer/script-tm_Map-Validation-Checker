namespace MapValidationChecker.CharacterizationTests;

public sealed class CliArgumentContractTests
{
    [Fact]
    public async Task No_arguments_returns_usage_as_an_argument_error()
    {
        var result = await CliProcess.RunAsync();

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(ReadHelpSnapshot(), CliProcess.TrimFinalNewlines(result.StandardOutput));
        Assert.Equal("No arguments provided.", CliProcess.TrimFinalNewlines(result.StandardError));
    }

    [Fact]
    public async Task Help_flag_currently_returns_the_same_argument_error_as_no_arguments()
    {
        var result = await CliProcess.RunAsync("--help");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(ReadHelpSnapshot(), CliProcess.TrimFinalNewlines(result.StandardOutput));
        Assert.Equal("No arguments provided.", CliProcess.TrimFinalNewlines(result.StandardError));
    }

    [Fact]
    public async Task Unknown_flag_returns_the_flag_error_and_usage()
    {
        var result = await CliProcess.RunAsync("--wat");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(ReadHelpSnapshot(), CliProcess.TrimFinalNewlines(result.StandardOutput));
        Assert.Equal("Unknown flag: --wat", CliProcess.TrimFinalNewlines(result.StandardError));
    }

    [Theory]
    [InlineData("--progress-interval", "0", "--progress-interval must be a positive number (seconds)")]
    [InlineData("--gps-threshold-ms", "-1", "--gps-threshold-ms must be a non-negative integer")]
    [InlineData("--max-depth", "-1", "--max-depth must be a non-negative integer")]
    public async Task Invalid_numeric_options_return_argument_errors(
        string option,
        string value,
        string expectedError)
    {
        var result = await CliProcess.RunAsync(option, value);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(ReadHelpSnapshot(), CliProcess.TrimFinalNewlines(result.StandardOutput));
        Assert.Equal(expectedError, CliProcess.TrimFinalNewlines(result.StandardError));
    }

    [Fact]
    public async Task Missing_option_value_names_the_option()
    {
        var result = await CliProcess.RunAsync("--single");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(ReadHelpSnapshot(), CliProcess.TrimFinalNewlines(result.StandardOutput));
        Assert.Equal("Missing value after --single", CliProcess.TrimFinalNewlines(result.StandardError));
    }

    private static string ReadHelpSnapshot()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Snapshots", "help.stdout.txt");
        return CliProcess.TrimFinalNewlines(File.ReadAllText(path));
    }
}
