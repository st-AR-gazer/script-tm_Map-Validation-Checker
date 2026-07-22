using GBX.NET.Engines.Game;

using MapValidationChecker.Cli.Evidence;
using MapValidationChecker.Cli.Infrastructure;
using MapValidationChecker.Core.Validation;

namespace MapValidationChecker.Cli.Tests;

public sealed class EvidenceReaderTests
{
    [Fact]
    public async Task Manual_override_catalog_preserves_legacy_boolean_support_and_defaults()
    {
        using var directory = new TemporaryDirectory();
        var manualPath = directory.GetPath("manual.json");
        await File.WriteAllTextAsync(
            manualPath,
            """
            [
              { "uid": "reviewed", "valid": False, "note": "checked manually" },
              { "uid": "default-valid" }
            ]
            """);

        var catalog = ManualOverrideCatalog.Load(manualPath);

        Assert.Equal(
            new ManualOverrideEvidence(false, "checked manually"),
            catalog.Find("reviewed"));
        Assert.Equal(
            new ManualOverrideEvidence(true, null),
            catalog.Find("default-valid"));
        Assert.Null(catalog.Find("REVIEWED"));
        Assert.Null(catalog.Find(null));

        var mapEvidence = NonGpsEvidenceReader.Read(
            new CGameCtnChallenge { MapUid = "reviewed" },
            catalog,
            ReplayEvidenceIndex.Empty);
        Assert.Equal(
            new ManualOverrideEvidence(false, "checked manually"),
            mapEvidence.ManualOverride);
    }

    [Theory]
    [InlineData("1:03.502", 63_502)]
    [InlineData("1:02:03.004", 3_723_004)]
    [InlineData("12.5", 12_500)]
    [InlineData("12.5678", 12_567)]
    public void Gbx_time_parser_preserves_supported_formats(string value, int expected)
    {
        Assert.True(GbxTime.TryParse(value, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Non_gps_reader_returns_empty_evidence_for_an_empty_map()
    {
        var evidence = NonGpsEvidenceReader.Read(
            new CGameCtnChallenge(),
            ManualOverrideCatalog.Empty,
            ReplayEvidenceIndex.Empty);

        Assert.Null(evidence.AuthorTimeMs);
        Assert.Null(evidence.ManualOverride);
        Assert.Null(evidence.ValidationGhostTimeMs);
        Assert.Null(evidence.ValidationTag);
        Assert.Null(evidence.MatchingReplay);
        Assert.Null(evidence.WaypointMetadata);
        Assert.Equal(new MapCheckpointFacts(0, false, 0, 0), evidence.Checkpoints);
    }

    [Fact]
    public void Record_data_entries_expand_to_all_compatible_candidate_sources()
    {
        var entry = new GpsRecordDataEntryDump(
            "RecordData.EntList[0]",
            EntListCount: 1,
            IsNull: false,
            U01: null,
            U02: null,
            U03: 13_000,
            SamplesCount: 2,
            LastSampleIndex: 1,
            LastSampleTimeMs: 9_990,
            Samples2Count: 3,
            LastSample2Index: 2,
            LastSample2TimeMs: 10_010);

        var candidates = GpsCandidateExtractor
            .EnumerateCandidatesFromEntries([entry])
            .ToArray();

        Assert.Equal(
            [13_000, 10_000, 9_990, 10_010],
            candidates.Select(candidate => candidate.TimeMs));
        Assert.Equal(
            [
                "RecordData.EntList[0].U03",
                "RecordData.EntList[0].U03MinusCountdown",
                "RecordData.EntList[0].Samples[1].Time",
                "RecordData.EntList[0].Samples2[2].Time"
            ],
            candidates.Select(candidate => candidate.Source));
    }

    [Fact]
    public void Threshold_match_chooses_the_closest_candidate()
    {
        GpsCandidate[] candidates =
        [
            new(9_950, "first.U03"),
            new(10_020, "second.U03"),
            new(10_200, "outside.U03")
        ];

        var match = GpsEvidenceReader.FindBestThresholdMatch(
            candidates,
            authorTimeMs: 10_000,
            thresholdMs: 100,
            static source => source.EndsWith(".U03", StringComparison.Ordinal));

        Assert.NotNull(match);
        Assert.Equal(10_020, match.GpsTimeMs);
        Assert.Equal(20, match.DeltaMs);
        Assert.Equal("second.U03", match.Source);
        Assert.Equal(GpsMatchMethod.U03Threshold, match.Method);
        Assert.Equal(GpsMatchKind.ThresholdMatch, match.Kind);
    }

    [Fact]
    public void Reflection_traversal_honors_the_maximum_depth()
    {
        var marker = new ReflectionMarker();
        var root = new ReflectionNode
        {
            Child = new ReflectionNode { Marker = marker }
        };

        Assert.Empty(GbxReflection.TraverseForType<ReflectionMarker>(root, maxDepth: 1));
        Assert.Same(
            marker,
            Assert.Single(GbxReflection.TraverseForType<ReflectionMarker>(root, maxDepth: 2)));
    }

    private sealed class ReflectionNode
    {
        public ReflectionNode? Child { get; init; }
        public ReflectionMarker? Marker { get; init; }
    }

    private sealed class ReflectionMarker;
}
