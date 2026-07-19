using System.Text.Json;

namespace MapValidationChecker.CharacterizationTests;

public sealed class CliFileContractTests
{
    private static readonly string[] ReportPropertyNames =
    [
        "uid",
        "validated",
        "type",
        "note",
        "gpsValidation",
        "path",
        "mapName",
        "replayPath",
        "error",
        "dataDump"
    ];

    [Fact]
    public async Task Non_gbx_single_file_returns_a_successful_error_report_with_null_fields()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = directory.GetPath("not-a-map.txt");
        await File.WriteAllTextAsync(inputPath, "not a GBX file");

        var result = await CliProcess.RunAsync("--single", inputPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, CliProcess.TrimFinalNewlines(result.StandardError));

        using var json = JsonDocument.Parse(result.StandardOutput);
        var report = json.RootElement;
        Assert.Equal(JsonValueKind.Object, report.ValueKind);
        AssertReportShape(report);
        Assert.Equal("not a gbx file", report.GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.Null, report.GetProperty("path").ValueKind);

        foreach (var propertyName in ReportPropertyNames.Where(name => name != "error"))
            Assert.Equal(JsonValueKind.Null, report.GetProperty(propertyName).ValueKind);
    }

    [Fact]
    public async Task Include_path_populates_the_path_even_for_a_non_gbx_report()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = directory.GetPath("not-a-map.txt");
        await File.WriteAllTextAsync(inputPath, "not a GBX file");

        var result = await CliProcess.RunAsync("--single", inputPath, "--include-path");

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal(inputPath, json.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public async Task Gbx_magic_with_invalid_content_returns_a_parse_error_report()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = directory.GetPath("broken.Map.Gbx");
        await File.WriteAllBytesAsync(inputPath, [0x47, 0x42, 0x58, 0x00, 0x01, 0x02]);

        var result = await CliProcess.RunAsync("--single", inputPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, CliProcess.TrimFinalNewlines(result.StandardError));

        using var json = JsonDocument.Parse(result.StandardOutput);
        var report = json.RootElement;
        AssertReportShape(report);
        Assert.Equal("failed to parse map gbx", report.GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.String, report.GetProperty("note").ValueKind);
        Assert.Contains(":", report.GetProperty("note").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Batch_mode_returns_an_array_and_includes_every_file()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(directory.GetPath("first.txt"), "first");
        await File.WriteAllTextAsync(directory.GetPath("second.txt"), "second");

        var result = await CliProcess.RunAsync("--batch", directory.Path, "--include-path");

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);

        var reports = json.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, reports.Length);
        Assert.All(reports, report =>
        {
            AssertReportShape(report);
            Assert.Equal("not a gbx file", report.GetProperty("error").GetString());
        });

        var actualPaths = reports
            .Select(report => report.GetProperty("path").GetString())
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var expectedPaths = new[]
        {
            directory.GetPath("first.txt"),
            directory.GetPath("second.txt")
        }.OrderBy(path => path, StringComparer.Ordinal).ToArray();

        Assert.Equal(expectedPaths, actualPaths);
    }

    [Fact]
    public async Task Recursive_flag_controls_whether_batch_mode_visits_nested_files()
    {
        using var directory = new TemporaryDirectory();
        var nestedDirectory = directory.GetPath("nested");
        Directory.CreateDirectory(nestedDirectory);
        await File.WriteAllTextAsync(directory.GetPath("root.txt"), "root");
        await File.WriteAllTextAsync(Path.Combine(nestedDirectory, "nested.txt"), "nested");

        var topLevel = await CliProcess.RunAsync("--batch", directory.Path);
        var recursive = await CliProcess.RunAsync("--batch", directory.Path, "--recursive");

        Assert.Equal(0, topLevel.ExitCode);
        Assert.Equal(0, recursive.ExitCode);

        using var topLevelJson = JsonDocument.Parse(topLevel.StandardOutput);
        using var recursiveJson = JsonDocument.Parse(recursive.StandardOutput);
        Assert.Single(topLevelJson.RootElement.EnumerateArray());
        Assert.Equal(2, recursiveJson.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Pretty_output_is_indented_but_semantically_identical()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = directory.GetPath("not-a-map.txt");
        await File.WriteAllTextAsync(inputPath, "not a GBX file");

        var compact = await CliProcess.RunAsync("--single", inputPath);
        var pretty = await CliProcess.RunAsync("--single", inputPath, "--pretty");

        Assert.Equal(0, compact.ExitCode);
        Assert.Equal(0, pretty.ExitCode);
        Assert.Contains('\n', CliProcess.NormalizeLineEndings(pretty.StandardOutput));

        using var compactJson = JsonDocument.Parse(compact.StandardOutput);
        using var prettyJson = JsonDocument.Parse(pretty.StandardOutput);
        Assert.Equal(
            compactJson.RootElement.GetRawText(),
            JsonSerializer.Serialize(prettyJson.RootElement));
    }

    [Fact]
    public async Task Output_option_writes_the_same_json_that_is_printed_to_stdout()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = directory.GetPath("not-a-map.txt");
        var outputPath = directory.GetPath(Path.Combine("nested", "report.json"));
        await File.WriteAllTextAsync(inputPath, "not a GBX file");

        var result = await CliProcess.RunAsync(
            "--single",
            inputPath,
            "--output",
            outputPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath));
        Assert.Equal(
            CliProcess.TrimFinalNewlines(result.StandardOutput),
            CliProcess.NormalizeLineEndings(await File.ReadAllTextAsync(outputPath)));
    }

    [Fact]
    public async Task Missing_single_file_is_an_argument_error()
    {
        using var directory = new TemporaryDirectory();
        var missingPath = directory.GetPath("missing.Map.Gbx");

        var result = await CliProcess.RunAsync("--single", missingPath);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(
            $"Map file does not exist: {missingPath}",
            CliProcess.TrimFinalNewlines(result.StandardError));
    }

    [Fact]
    public async Task Unhandled_output_failure_uses_the_fatal_exit_contract()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = directory.GetPath("not-a-map.txt");
        var outputDirectory = directory.GetPath("existing-directory");
        await File.WriteAllTextAsync(inputPath, "not a GBX file");
        Directory.CreateDirectory(outputDirectory);

        var result = await CliProcess.RunAsync(
            "--single",
            inputPath,
            "--output",
            outputDirectory);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, CliProcess.TrimFinalNewlines(result.StandardOutput));
        Assert.StartsWith(
            "Fatal error: ",
            CliProcess.TrimFinalNewlines(result.StandardError),
            StringComparison.Ordinal);
    }

    private static void AssertReportShape(JsonElement report)
    {
        var actualNames = report
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var expectedNames = ReportPropertyNames
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedNames, actualNames);
    }
}
