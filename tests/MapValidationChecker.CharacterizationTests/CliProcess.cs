using System.Diagnostics;

namespace MapValidationChecker.CharacterizationTests;

internal static class CliProcess
{
    private const string ApplicationAssemblyName = "MapValidationChecker.dll";

    public static async Task<CliRunResult> RunAsync(params string[] arguments)
    {
        var applicationPath = Path.Combine(AppContext.BaseDirectory, ApplicationAssemblyName);
        if (!File.Exists(applicationPath))
        {
            throw new FileNotFoundException(
                "The application assembly was not copied to the test output directory.",
                applicationPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(applicationPath);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the application process.");

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return new CliRunResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    public static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal);

    public static string TrimFinalNewlines(string value) =>
        NormalizeLineEndings(value).TrimEnd('\n');
}

internal sealed record CliRunResult(int ExitCode, string StandardOutput, string StandardError);
