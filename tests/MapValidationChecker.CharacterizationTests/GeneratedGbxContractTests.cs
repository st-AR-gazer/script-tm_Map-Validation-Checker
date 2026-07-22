using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.LZO;

namespace MapValidationChecker.CharacterizationTests;

public sealed class GeneratedGbxContractTests
{
    [Fact]
    public async Task Empty_generated_map_preserves_the_missing_author_time_contract()
    {
        using var directory = new TemporaryDirectory();
        var mapPath = directory.GetPath("empty.Map.Gbx");
        var map = new CGameCtnChallenge();

        Gbx.LZO = new Lzo();
        map.Save(mapPath);

        var result = await CliProcess.RunAsync("--single", mapPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, CliProcess.TrimFinalNewlines(result.StandardError));

        using var json = JsonDocument.Parse(result.StandardOutput);
        var report = json.RootElement;
        Assert.Equal(string.Empty, report.GetProperty("uid").GetString());
        Assert.Equal("Unknown", report.GetProperty("validated").GetString());
        Assert.Equal("normal", report.GetProperty("type").GetString());
        Assert.Equal(
            "Map is missing author time; validation checks skipped.",
            report.GetProperty("note").GetString());
        Assert.Equal(string.Empty, report.GetProperty("mapName").GetString());
        Assert.Equal("missing AuthorMedal time", report.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Empty_generated_map_data_dump_preserves_the_diagnostic_contract()
    {
        using var directory = new TemporaryDirectory();
        var mapPath = directory.GetPath("empty.Map.Gbx");
        var map = new CGameCtnChallenge();

        Gbx.LZO = new Lzo();
        map.Save(mapPath);

        var result = await CliProcess.RunAsync("--single", mapPath, "--data-dump");

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.StandardOutput);
        var dataDump = json.RootElement.GetProperty("dataDump");

        Assert.Equal(0, dataDump.GetProperty("nbCheckpoints").GetInt32());
        Assert.False(dataDump.GetProperty("isLapRace").GetBoolean());
        Assert.Equal(0, dataDump.GetProperty("nbLaps").GetInt32());
        Assert.Equal(0, dataDump.GetProperty("expectedWaypointCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, dataDump.GetProperty("effectiveAuthorTimeMs").ValueKind);
        Assert.Equal(JsonValueKind.Null, dataDump.GetProperty("validationTag").ValueKind);
        Assert.Equal(JsonValueKind.Null, dataDump.GetProperty("gpsRecordDataEntries").ValueKind);
        Assert.Equal(JsonValueKind.Null, dataDump.GetProperty("mediaBlockEntityChunkCandidates").ValueKind);
    }
}
