using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.Script;
using GBX.NET.LZO;
using GBX.NET.ZLib;

internal sealed class Program
{
    private const string WaypointTimesKey = "Race_AuthorRaceWaypointTimes";
    private const int DefaultGpsThresholdMs = 100;
    private const int GpsCountdownOffsetMs = 3000;
    private const string ValidationRemovalSignatureText = "RaceValidationReplay Remover made by ar";
    private static readonly string ValidationRemovalSignatureHex = BuildSignatureHexString(ValidationRemovalSignatureText);

    private static int Main(string[] args)
    {
        try
        {
            var opts = ParseArgs(args);

            Gbx.LZO = new Lzo();
            Gbx.ZLib = new ZLib();

            var manual = !string.IsNullOrWhiteSpace(opts.ManualPath)
                ? LoadManualOverrides(opts.ManualPath!)
                : new Dictionary<string, ManualEntry>(StringComparer.Ordinal);

            var replayIndex = !string.IsNullOrWhiteSpace(opts.ReplaysPath)
                ? BuildReplayIndex(opts.ReplaysPath!, opts.Recursive, opts.Progress, opts.ProgressIntervalSeconds)
                : new Dictionary<string, List<ReplayEntry>>(StringComparer.Ordinal);

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
        Dictionary<string, ManualEntry> manual,
        Dictionary<string, List<ReplayEntry>> replayIndex)
    {
        var report = new Report();

        if (opts.IncludePath)
            report.Path = mapFilePath;

        if (!LooksLikeGbx(mapFilePath))
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

        var authorMs = TimeToMs(map.AuthorTime ?? map.ChallengeParameters?.AuthorTime);

        if (opts.DataDump)
            report.DataDump = BuildDataDump(map, authorMs, opts.MaxDepth);

        // 1) Manual override
        if (!string.IsNullOrWhiteSpace(report.Uid) &&
            manual.TryGetValue(report.Uid!, out var manualEntry))
        {
            report.Type = "manual";
            report.Validated = manualEntry.Valid ? "Yes" : "Maybe";
            report.Note = manualEntry.Note;
            return report;
        }

        if (!authorMs.HasValue)
        {
            report.Validated = "Unknown";
            report.Type = "normal";
            report.Error = "missing AuthorMedal time";
            report.Note = "Map is missing author time; validation checks skipped.";
            return report;
        }

        // 2) Validation ghost check
        var valGhost = map.ChallengeParameters?.RaceValidateGhost;
        if (valGhost is not null)
        {
            var valMs = TimeToMs(valGhost.RaceTime);
            if (valMs.HasValue)
            {
                if (valMs.Value == authorMs.Value)
                {
                    report.Validated = "Yes";
                    report.Type = "validationghost";
                    return report;
                }

                report.Validated = "Unknown";
                report.Type = "validationghost";
                report.Error = "validation ghost time mismatch";
                report.Note = $"authorTimeMs={authorMs.Value}, validationGhostMs={valMs.Value}";
                return report;
            }
        }

        // 3) Validation removal tag (script metadata)
        if (TryGetValidationRemovalTagInfo(map.ScriptMetadata, out var tagInfo))
        {
            report.Type = "validationtag";
            var signatureWarning = tagInfo.HasSignature
                ? null
                : "Warning: expected removal-tool signature is missing; metadata looks suspicious.";

            if (tagInfo.AuthorTimeMs.HasValue)
            {
                if (authorMs.Value == tagInfo.AuthorTimeMs.Value)
                {
                    report.Validated = "Yes";
                    report.Note = BuildValidationTagNote(
                        tagInfo,
                        JoinNonEmpty(
                            "Validation ghost removed (tag found in script metadata).",
                            signatureWarning));
                    return report;
                }

                report.Validated = "Maybe";
                report.Note = BuildValidationTagNote(
                    tagInfo,
                    JoinNonEmpty(
                        $"Warning: validation tag author time mismatch; authorTimeMs={authorMs.Value}, tagAuthorTimeMs={tagInfo.AuthorTimeMs.Value}.",
                        signatureWarning));
                return report;
            }

            report.Validated = "Maybe";
            report.Note = BuildValidationTagNote(
                tagInfo,
                JoinNonEmpty(
                    "Validation-removal tag found, but no tag author time was extracted.",
                    signatureWarning));
            return report;
        }

        // 4) Replay mapping (external evidence)
        if (!string.IsNullOrWhiteSpace(report.Uid) && authorMs.HasValue &&
            replayIndex.TryGetValue(report.Uid!, out var replayEntries))
        {
            var match = replayEntries.FirstOrDefault(r => r.GhostTimesMs.Contains(authorMs.Value));
            if (match is not null)
            {
                report.Validated = "Yes";
                report.Type = "replay";
                report.Note = "Replay ghost time matched map author time.";
                if (opts.IncludePath)
                    report.ReplayPath = match.Path;
                return report;
            }
        }

        // 5) Script metadata check => normal vs plugin suspicion
        var wpTimes = ExtractWaypointTimes(map.ScriptMetadata);
        if (wpTimes is not null && wpTimes.Count > 0)
        {
            var metadataFinish = wpTimes[wpTimes.Count - 1];
            var cpCountLooksWeird = !(map.NbCheckpoints == wpTimes.Count || (map.NbCheckpoints + 1) == wpTimes.Count);
            var finishMatchesAuthor = metadataFinish == authorMs.Value;

            if (finishMatchesAuthor)
            {
                if (cpCountLooksWeird)
                {
                    report.Validated = "Maybe";
                    report.Type = "plugin";
                    report.Note = $"Finish time matches, but checkpoint count differs (mapNbCheckpoints={map.NbCheckpoints}, metadataWaypoints={wpTimes.Count}).";
                    return report;
                }

                report.Validated = "Yes";
                report.Type = "normal";
                return report;
            }
        }

        // 6) GPS check (optional):
        if (opts.GpsEnabled)
        {
            if (HasGpsGhostAtAuthorTime(map, authorMs.Value, opts.MaxDepth, opts.GpsThresholdMs, out var gpsMatch))
            {
                report.Type = "gps";
                report.Validated = opts.StrictGps ? "Yes" : "Maybe";
                if (gpsMatch is not null)
                {
                    report.GpsValidation = BuildGpsValidationDetails(authorMs.Value, opts.GpsThresholdMs, gpsMatch);
                    report.Note = BuildGpsValidationNote(opts.GpsThresholdMs, opts.StrictGps, gpsMatch);
                }
                return report;
            }
        }

        if (wpTimes is null || wpTimes.Count == 0)
        {
            report.Validated = "Unknown";
            report.Type = "normal";
            report.Note = "Missing author time or Race_AuthorRaceWaypointTimes metadata.";
            return report;
        }

        var metadataFinishFinal = wpTimes[wpTimes.Count - 1];
        report.Validated = "Maybe";
        report.Type = "plugin";
        report.Note = $"AuthorTime differs from metadata finish (authorTimeMs={authorMs.Value}, metadataFinishMs={metadataFinishFinal}, mapNbCheckpoints={map.NbCheckpoints}, metadataWaypoints={wpTimes.Count}).";
        return report;

    }

    // ----------------------------
    // Replay index
    // ----------------------------

    private static Dictionary<string, List<ReplayEntry>> BuildReplayIndex(
        string path,
        bool recursive,
        bool progressEnabled,
        double progressIntervalSeconds)
    {
        var dict = new Dictionary<string, List<ReplayEntry>>(StringComparer.Ordinal);

        var files = EnumerateFiles(path, recursive).ToList();
        var total = files.Count;
        var progress = progressEnabled ? new ProgressReporter(TimeSpan.FromSeconds(progressIntervalSeconds)) : null;
        int scanned = 0;
        int gbxCount = 0;
        int indexed = 0;

        foreach (var file in files)
        {
            scanned++;

            if (!LooksLikeGbx(file))
            {
                if (progress is not null && progress.TryGetStats(scanned, out var replayStatsSkip))
                {
                    var eta = ProgressReporter.GetEta(total - scanned, replayStatsSkip.AvgRate);
                    Console.Error.WriteLine(
                        $"Replay scan: {scanned}/{total} files, gbx={gbxCount}, indexed={indexed}, rate={replayStatsSkip.AvgRate:F1}/s (last {replayStatsSkip.IntervalRate:F1}/s), eta={eta}, elapsed={replayStatsSkip.Elapsed}");
                }
                continue;
            }

            try
            {
                gbxCount++;

                var replay = Gbx.ParseNode<CGameCtnReplayRecord>(file);

                var uid = replay.MapInfo?.Id;

                uid ??= replay.Challenge?.MapUid;
                uid ??= replay.Ghosts?.FirstOrDefault()?.Validate_ChallengeUid;

                if (string.IsNullOrWhiteSpace(uid))
                    continue;

                var times = new HashSet<int>();

                foreach (var ghost in replay.GetGhosts(alsoInClips: true))
                {
                    var t = TimeToMs(ghost.RaceTime);
                    if (t.HasValue)
                        times.Add(t.Value);
                }

                if (times.Count == 0)
                {
                    if (progress is not null && progress.TryGetStats(scanned, out var replayStatsNoTimes))
                    {
                        var eta = ProgressReporter.GetEta(total - scanned, replayStatsNoTimes.AvgRate);
                        Console.Error.WriteLine(
                            $"Replay scan: {scanned}/{total} files, gbx={gbxCount}, indexed={indexed}, rate={replayStatsNoTimes.AvgRate:F1}/s (last {replayStatsNoTimes.IntervalRate:F1}/s), eta={eta}, elapsed={replayStatsNoTimes.Elapsed}");
                    }
                    continue;
                }

                if (!dict.TryGetValue(uid!, out var list))
                {
                    list = new List<ReplayEntry>();
                    dict[uid!] = list;
                }

                list.Add(new ReplayEntry(file, times));
                indexed++;
            }
            catch { }

            if (progress is not null && progress.TryGetStats(scanned, out var replayStatsOk))
            {
                var eta = ProgressReporter.GetEta(total - scanned, replayStatsOk.AvgRate);
                Console.Error.WriteLine(
                    $"Replay scan: {scanned}/{total} files, gbx={gbxCount}, indexed={indexed}, rate={replayStatsOk.AvgRate:F1}/s (last {replayStatsOk.IntervalRate:F1}/s), eta={eta}, elapsed={replayStatsOk.Elapsed}");
            }
        }

        return dict;
    }

    // ----------------------------
    // Manual overrides
    // ----------------------------

    private static Dictionary<string, ManualEntry> LoadManualOverrides(string filePath)
    {
        var dict = new Dictionary<string, ManualEntry>(StringComparer.Ordinal);

        var raw = File.ReadAllText(filePath);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(raw);
        }
        catch
        {
            raw = raw.Replace("True", "true").Replace("False", "false");
            doc = JsonDocument.Parse(raw);
        }

        void AddEntry(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object)
                return;

            if (!el.TryGetProperty("uid", out var uidProp))
                return;

            var uid = uidProp.GetString();
            if (string.IsNullOrWhiteSpace(uid))
                return;

            var valid = el.TryGetProperty("valid", out var validProp) && validProp.ValueKind == JsonValueKind.True
                ? true
                : el.TryGetProperty("valid", out validProp) && validProp.ValueKind == JsonValueKind.False
                    ? false
                    : true;

            string? note = null;
            if (el.TryGetProperty("note", out var noteProp) && noteProp.ValueKind == JsonValueKind.String)
                note = noteProp.GetString();

            dict[uid!] = new ManualEntry(valid, note);
        }

        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            AddEntry(doc.RootElement);
        }
        else if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in doc.RootElement.EnumerateArray())
                AddEntry(el);
        }

        return dict;
    }

    // ----------------------------
    // GPS scan
    // ----------------------------

    private static bool HasGpsGhostAtAuthorTime(
        CGameCtnChallenge map,
        int authorMs,
        int? maxDepth,
        int gpsThresholdMs,
        out GpsMatchInfo? matchInfo)
    {
        matchInfo = null;

        if (map.ClipGroupInGame is null)
            return false;

        foreach (var candidate in EnumerateMediaBlockEntityChunkCandidates(map))
        {
            if (candidate.TimeMs == authorMs)
            {
                matchInfo = new GpsMatchInfo(
                    candidate.TimeMs,
                    0,
                    candidate.Source,
                    GpsMatchMethod.U05Exact,
                    GpsMatchKind.ExactMatch);
                return true;
            }
        }

        var recordDataCandidates = EnumerateGpsRecordDataCandidates(map).ToList();
        if (TryFindBestThresholdMatch(
                recordDataCandidates,
                authorMs,
                gpsThresholdMs,
                static source => source.EndsWith(".U03", StringComparison.Ordinal),
                out matchInfo))
        {
            return true;
        }

        if (TryFindBestThresholdMatch(
                recordDataCandidates,
                authorMs,
                gpsThresholdMs,
                static source => source.EndsWith(".U03MinusCountdown", StringComparison.Ordinal),
                out matchInfo))
        {
            return true;
        }

        return false;
    }

    private static bool TryFindBestThresholdMatch(
        IEnumerable<GpsCandidate> candidates,
        int authorMs,
        int thresholdMs,
        Func<string, bool> sourceFilter,
        out GpsMatchInfo? matchInfo)
    {
        matchInfo = null;

        GpsCandidate? bestCandidate = null;
        int bestDelta = int.MaxValue;

        foreach (var candidate in candidates)
        {
            if (!sourceFilter(candidate.Source))
                continue;

            var delta = Math.Abs(candidate.TimeMs - authorMs);
            if (delta > thresholdMs)
                continue;

            if (bestCandidate is null ||
                delta < bestDelta ||
                (delta == bestDelta && GetGpsSourcePriority(candidate.Source) < GetGpsSourcePriority(bestCandidate.Source)))
            {
                bestCandidate = candidate;
                bestDelta = delta;
            }
        }

        if (bestCandidate is null)
            return false;

        matchInfo = new GpsMatchInfo(
            bestCandidate.TimeMs,
            bestDelta,
            bestCandidate.Source,
            GetThresholdMatchMethod(bestCandidate.Source),
            GpsMatchKind.ThresholdMatch);
        return true;
    }

    private static int GetGpsSourcePriority(string source)
    {
        if (source.EndsWith(".U03", StringComparison.Ordinal))
            return 0;

        if (source.EndsWith(".U03MinusCountdown", StringComparison.Ordinal))
            return 1;

        if (source.Contains(".Samples2[", StringComparison.Ordinal))
            return 2;

        if (source.Contains(".Samples[", StringComparison.Ordinal))
            return 3;

        if (source.EndsWith(".RaceTime", StringComparison.Ordinal))
            return 4;

        return 5;
    }

    private static GpsMatchMethod GetThresholdMatchMethod(string source)
    {
        if (source.EndsWith(".U03", StringComparison.Ordinal))
            return GpsMatchMethod.U03Threshold;

        if (source.EndsWith(".U03MinusCountdown", StringComparison.Ordinal))
            return GpsMatchMethod.U03MinusCountdownThreshold;

        return GpsMatchMethod.U03Threshold;
    }

    private static DataDump BuildDataDump(CGameCtnChallenge map, int? effectiveAuthorTimeMs, int? maxDepth)
    {
        var cp = map.ChallengeParameters;
        var validationGhostMs = TimeToMs(cp?.RaceValidateGhost?.RaceTime);

        var metadataKeys = map.ScriptMetadata?.Traits?.Keys
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        var waypointTimes = ExtractWaypointTimes(map.ScriptMetadata);

        ValidationTagDump? validationTag = null;
        if (TryGetValidationRemovalTagInfo(map.ScriptMetadata, out var tagInfo))
        {
            validationTag = new ValidationTagDump
            {
                Key = tagInfo.Key,
                Note = tagInfo.Note,
                AuthorTimeMs = tagInfo.AuthorTimeMs,
                AuthorTimeSource = tagInfo.AuthorTimeSource,
                SignaturePresent = tagInfo.HasSignature
            };
        }

        var gpsRecordDataEntries = CollectGpsRecordDataEntries(map);
        var gpsRecordDataCandidates = EnumerateGpsCandidatesFromEntries(gpsRecordDataEntries).ToList();
        var mediaBlockEntityChunkCandidates = EnumerateMediaBlockEntityChunkCandidates(map).ToList();
        var clipGroupEntLists = CollectClipGroupEntListSummaries(map, maxDepth);
        var entRecordElemCandidates = EnumerateEntRecordElemCandidates(map, maxDepth).ToList();

        var mediaBlockGhostCandidates = CollectMediaBlockGhostCandidates(map, maxDepth);

        return new DataDump
        {
            NbCheckpoints = map.NbCheckpoints,
            MapAuthorTimeMs = TimeToMs(map.AuthorTime),
            ChallengeParametersAuthorTimeMs = TimeToMs(cp?.AuthorTime),
            EffectiveAuthorTimeMs = effectiveAuthorTimeMs,
            ValidationGhostRaceTimeMs = validationGhostMs,
            ScriptMetadataKeys = metadataKeys is { Count: > 0 } ? metadataKeys : null,
            WaypointTimesMs = waypointTimes is { Count: > 0 } ? waypointTimes : null,
            ValidationTag = validationTag,
            GpsRecordDataEntries = gpsRecordDataEntries.Count > 0 ? gpsRecordDataEntries : null,
            GpsRecordDataCandidates = gpsRecordDataCandidates.Count > 0 ? gpsRecordDataCandidates : null,
            MediaBlockEntityChunkCandidates = mediaBlockEntityChunkCandidates.Count > 0 ? mediaBlockEntityChunkCandidates : null,
            ClipGroupEntLists = clipGroupEntLists.Count > 0 ? clipGroupEntLists : null,
            EntRecordElemCandidates = entRecordElemCandidates.Count > 0 ? entRecordElemCandidates : null,
            MediaBlockGhostCandidates = mediaBlockGhostCandidates.Count > 0 ? mediaBlockGhostCandidates : null
        };
    }

    private static List<GpsRecordDataEntryDump> CollectGpsRecordDataEntries(CGameCtnChallenge map)
    {
        var list = new List<GpsRecordDataEntryDump>();

        var clipGroup = map.ClipGroupInGame;
        if (clipGroup?.Clips is null || clipGroup.Clips.Count == 0)
            return list;

        for (int triggerIndex = 0; triggerIndex < clipGroup.Clips.Count; triggerIndex++)
        {
            var trigger = clipGroup.Clips[triggerIndex];
            var clip = trigger.Clip;
            if (clip?.Tracks is null || clip.Tracks.Count == 0)
                continue;

            for (int trackIndex = 0; trackIndex < clip.Tracks.Count; trackIndex++)
            {
                var track = clip.Tracks[trackIndex];
                if (track?.Blocks is null || track.Blocks.Count == 0)
                    continue;

                for (int blockIndex = 0; blockIndex < track.Blocks.Count; blockIndex++)
                {
                    var block = track.Blocks[blockIndex];
                    if (block is null)
                        continue;

                    if (!TryGetMemberValue(block, "RecordData", out var recordDataObj) || recordDataObj is null)
                        continue;

                    if (!TryGetMemberValue(recordDataObj, "EntList", out var entListObj) ||
                        entListObj is null ||
                        entListObj is string ||
                        entListObj is not IEnumerable entList)
                        continue;

                    int? entListCount = TryGetCollectionCount(entListObj, out var countValue) ? countValue : null;

                    foreach (var entItem in EnumerateCollectionItems(entListObj))
                    {
                        int entIndex = entItem.Index;
                        var ent = entItem.Value;

                        if (ent is null)
                        {
                            var nullBasePath =
                                $"ClipGroupInGame.Clips[{triggerIndex}].Clip.Tracks[{trackIndex}].Blocks[{blockIndex}].RecordData.EntList[{entIndex}]";
                            list.Add(new GpsRecordDataEntryDump(
                                nullBasePath,
                                entListCount,
                                true,
                                null,
                                null,
                                null,
                                0,
                                null,
                                null,
                                0,
                                null,
                                null));
                            continue;
                        }

                        var basePath =
                            $"ClipGroupInGame.Clips[{triggerIndex}].Clip.Tracks[{trackIndex}].Blocks[{blockIndex}].RecordData.EntList[{entIndex}]";

                        var u01 = TryGetIntPropertyValue(ent, "U01");
                        var u02 = TryGetIntPropertyValue(ent, "U02");
                        var u03 = TryGetIntPropertyValue(ent, "U03");

                        var samples = GetSampleCollectionSummary(ent, "Samples");
                        var samples2 = GetSampleCollectionSummary(ent, "Samples2");

                        list.Add(new GpsRecordDataEntryDump(
                            basePath,
                            entListCount,
                            false,
                            u01,
                            u02,
                            u03,
                            samples.Count,
                            samples.LastIndexWithTime,
                            samples.LastTimeMs,
                            samples2.Count,
                            samples2.LastIndexWithTime,
                            samples2.LastTimeMs));
                    }
                }
            }
        }

        return list;
    }

    private static List<GpsCandidate> CollectMediaBlockGhostCandidates(CGameCtnChallenge map, int? maxDepth)
    {
        var list = new List<GpsCandidate>();
        if (map.ClipGroupInGame is null)
            return list;

        int index = 0;
        foreach (var block in TraverseForType<CGameCtnMediaBlockGhost>(map.ClipGroupInGame, maxDepth))
        {
            var ghost = block.GhostModel;
            if (ghost is null)
            {
                index++;
                continue;
            }

            var t = TimeToMs(ghost.RaceTime);
            if (t.HasValue)
            {
                list.Add(new GpsCandidate(
                    t.Value,
                    $"CGameCtnMediaBlockGhost[{index}].GhostModel.RaceTime"));
            }

            index++;
        }

        return list;
    }

    private static IEnumerable<GpsCandidate> EnumerateMediaBlockEntityChunkCandidates(CGameCtnChallenge map)
    {
        var clipGroup = map.ClipGroupInGame;
        if (clipGroup?.Clips is null || clipGroup.Clips.Count == 0)
            yield break;

        for (int triggerIndex = 0; triggerIndex < clipGroup.Clips.Count; triggerIndex++)
        {
            var trigger = clipGroup.Clips[triggerIndex];
            var clip = trigger.Clip;
            if (clip?.Tracks is null || clip.Tracks.Count == 0)
                continue;

            for (int trackIndex = 0; trackIndex < clip.Tracks.Count; trackIndex++)
            {
                var track = clip.Tracks[trackIndex];
                if (track?.Blocks is null || track.Blocks.Count == 0)
                    continue;

                for (int blockIndex = 0; blockIndex < track.Blocks.Count; blockIndex++)
                {
                    var block = track.Blocks[blockIndex];
                    if (block is null)
                        continue;

                    if (!TryGetMemberValue(block, "GhostName", out var ghostNameObj) ||
                        ghostNameObj is not string)
                    {
                        continue;
                    }

                    if (!TryGetMemberValue(block, "Chunks", out var chunksObj) || chunksObj is null)
                        continue;

                    foreach (var chunkItem in EnumerateCollectionItems(chunksObj))
                    {
                        var chunk = chunkItem.Value;
                        if (chunk is null)
                            continue;

                        var typeName = chunk.GetType().Name;
                        var u05 = TryGetIntPropertyValue(chunk, "U05");
                        if (!u05.HasValue)
                            continue;

                        yield return new GpsCandidate(
                            u05.Value,
                            $"ClipGroupInGame.Clips[{triggerIndex}].Clip.Tracks[{trackIndex}].Blocks[{blockIndex}].Chunks[{chunkItem.Index}].{typeName}.U05");
                    }
                }
            }
        }
    }

    private static List<EntListSummary> CollectClipGroupEntListSummaries(CGameCtnChallenge map, int? maxDepth)
    {
        var list = new List<EntListSummary>();
        var root = map.ClipGroupInGame;
        if (root is null)
            return list;

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<(object Obj, string Path, int Depth)>();
        stack.Push((root, "ClipGroupInGame", 0));

        while (stack.Count > 0)
        {
            var (obj, path, depth) = stack.Pop();
            if (obj is null || obj is string)
                continue;

            if (!visited.Add(obj))
                continue;

            if (TryGetMemberValue(obj, "EntList", out var entListObj) &&
                entListObj is not null &&
                entListObj is not string &&
                entListObj is IEnumerable)
            {
                int? entListCount = TryGetCollectionCount(entListObj, out var c) ? c : null;
                var sampledU03 = new List<int?>();
                foreach (var item in EnumerateCollectionItems(entListObj))
                {
                    sampledU03.Add(item.Value is null ? null : TryGetIntPropertyValue(item.Value, "U03"));
                    if (sampledU03.Count >= 32)
                        break;
                }

                list.Add(new EntListSummary($"{path}.EntList", entListCount, sampledU03));
            }

            if (maxDepth.HasValue && depth >= maxDepth.Value)
                continue;

            foreach (var child in EnumerateObjectChildrenWithPaths(obj, path))
                stack.Push((child.Obj, child.Path, depth + 1));
        }

        return list
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<GpsCandidate> EnumerateEntRecordElemCandidates(CGameCtnChallenge map, int? maxDepth)
    {
        foreach (var elem in EnumerateEntRecordElems(map, maxDepth))
        {
            if (elem.U03.HasValue && elem.U03.Value > 0)
                yield return new GpsCandidate(elem.U03.Value, $"{elem.Path}.U03");

            if (elem.U03MinusCountdownMs.HasValue)
                yield return new GpsCandidate(elem.U03MinusCountdownMs.Value, $"{elem.Path}.U03MinusCountdown");
        }
    }

    private static IEnumerable<EntRecordElemDump> EnumerateEntRecordElems(CGameCtnChallenge map, int? maxDepth)
    {
        var root = map.ClipGroupInGame;
        if (root is null)
            yield break;

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<(object Obj, string Path, int Depth)>();
        stack.Push((root, "ClipGroupInGame", 0));

        while (stack.Count > 0)
        {
            var (obj, path, depth) = stack.Pop();
            if (obj is null || obj is string)
                continue;

            if (!visited.Add(obj))
                continue;

            var typeName = obj.GetType().Name;
            if (typeName.EndsWith("EntRecordListElem", StringComparison.Ordinal))
            {
                var u03 = TryGetIntPropertyValue(obj, "U03");
                int? u03MinusCountdown = null;
                if (u03.HasValue && u03.Value >= GpsCountdownOffsetMs)
                    u03MinusCountdown = u03.Value - GpsCountdownOffsetMs;

                yield return new EntRecordElemDump(path, u03, u03MinusCountdown);
            }

            if (maxDepth.HasValue && depth >= maxDepth.Value)
                continue;

            foreach (var child in EnumerateObjectChildrenWithPaths(obj, path))
                stack.Push((child.Obj, child.Path, depth + 1));
        }
    }

    private static IEnumerable<GpsCandidate> EnumerateGpsRecordDataCandidates(CGameCtnChallenge map)
    {
        foreach (var candidate in EnumerateGpsCandidatesFromEntries(CollectGpsRecordDataEntries(map)))
            yield return candidate;
    }

    private static IEnumerable<GpsCandidate> EnumerateGpsCandidatesFromEntries(IEnumerable<GpsRecordDataEntryDump> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.U03.HasValue && entry.U03.Value > 0)
                yield return new GpsCandidate(entry.U03.Value, $"{entry.Path}.U03");

            if (entry.U03.HasValue && entry.U03.Value >= GpsCountdownOffsetMs)
            {
                yield return new GpsCandidate(
                    entry.U03.Value - GpsCountdownOffsetMs,
                    $"{entry.Path}.U03MinusCountdown");
            }

            if (entry.LastSampleTimeMs.HasValue && entry.LastSampleIndex.HasValue)
            {
                yield return new GpsCandidate(
                    entry.LastSampleTimeMs.Value,
                    $"{entry.Path}.Samples[{entry.LastSampleIndex.Value}].Time");
            }

            if (entry.LastSample2TimeMs.HasValue && entry.LastSample2Index.HasValue)
            {
                yield return new GpsCandidate(
                    entry.LastSample2TimeMs.Value,
                    $"{entry.Path}.Samples2[{entry.LastSample2Index.Value}].Time");
            }
        }
    }

    private static string BuildGpsValidationNote(int gpsThresholdMs, bool strictGps, GpsMatchInfo gpsMatch)
    {
        var methodNote = gpsMatch.Method switch
        {
            GpsMatchMethod.U05Exact => "GPS validated via exact U05 match (delta 0 ms).",
            GpsMatchMethod.U03Threshold => $"GPS validated via U03 within \u00b1{gpsThresholdMs} ms (delta {gpsMatch.DeltaMs} ms; not exact).",
            GpsMatchMethod.U03MinusCountdownThreshold => $"GPS validated via U03-3000 countdown normalization within \u00b1{gpsThresholdMs} ms (delta {gpsMatch.DeltaMs} ms; not exact).",
            _ => $"GPS validation used fallback value within \u00b1{gpsThresholdMs} ms."
        };

        return strictGps
            ? JoinNonEmpty(methodNote, "Strict mode => validated Yes.", "See gpsValidation for exact values.")
            : JoinNonEmpty(methodNote, "Still potentially invalid.", "See gpsValidation for exact values.");
    }

    private static GpsValidationDetails BuildGpsValidationDetails(int authorMs, int gpsThresholdMs, GpsMatchInfo gpsMatch)
    {
        var isExact = gpsMatch.Kind == GpsMatchKind.ExactMatch;

        return new GpsValidationDetails
        {
            MatchType = isExact ? "exact_match" : "within_threshold",
            Method = gpsMatch.Method switch
            {
                GpsMatchMethod.U05Exact => "u05_exact",
                GpsMatchMethod.U03Threshold => "u03_threshold",
                GpsMatchMethod.U03MinusCountdownThreshold => "u03_minus_countdown_threshold",
                _ => "u03_threshold"
            },
            AuthorTimeMs = authorMs,
            MatchedTimeMs = gpsMatch.GpsTimeMs,
            DeltaMs = gpsMatch.DeltaMs,
            ThresholdMs = isExact ? null : gpsThresholdMs,
            Source = gpsMatch.Source
        };
    }

    private static string JoinNonEmpty(params string?[] parts) => string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private enum GpsMatchKind
    {
        ExactMatch,
        ThresholdMatch
    }

    private enum GpsMatchMethod
    {
        U05Exact,
        U03Threshold,
        U03MinusCountdownThreshold
    }

    private sealed record GpsCandidate(int TimeMs, string Source);

    private sealed record GpsMatchInfo(
        int GpsTimeMs,
        int DeltaMs,
        string Source,
        GpsMatchMethod Method,
        GpsMatchKind Kind);

    private sealed class GpsValidationDetails
    {
        public string? MatchType { get; set; }
        public string? Method { get; set; }
        public int AuthorTimeMs { get; set; }
        public int MatchedTimeMs { get; set; }
        public int DeltaMs { get; set; }
        public int? ThresholdMs { get; set; }
        public string? Source { get; set; }
    }

    private sealed class DataDump
    {
        public int NbCheckpoints { get; set; }
        public int? MapAuthorTimeMs { get; set; }
        public int? ChallengeParametersAuthorTimeMs { get; set; }
        public int? EffectiveAuthorTimeMs { get; set; }
        public int? ValidationGhostRaceTimeMs { get; set; }
        public List<string>? ScriptMetadataKeys { get; set; }
        public List<int>? WaypointTimesMs { get; set; }
        public ValidationTagDump? ValidationTag { get; set; }
        public List<GpsRecordDataEntryDump>? GpsRecordDataEntries { get; set; }
        public List<GpsCandidate>? GpsRecordDataCandidates { get; set; }
        public List<GpsCandidate>? MediaBlockEntityChunkCandidates { get; set; }
        public List<EntListSummary>? ClipGroupEntLists { get; set; }
        public List<GpsCandidate>? EntRecordElemCandidates { get; set; }
        public List<GpsCandidate>? MediaBlockGhostCandidates { get; set; }
    }

    private sealed class ValidationTagDump
    {
        public string? Key { get; set; }
        public string? Note { get; set; }
        public int? AuthorTimeMs { get; set; }
        public string? AuthorTimeSource { get; set; }
        public bool SignaturePresent { get; set; }
    }

    private sealed record GpsRecordDataEntryDump(
        string Path,
        int? EntListCount,
        bool IsNull,
        int? U01,
        int? U02,
        int? U03,
        int SamplesCount,
        int? LastSampleIndex,
        int? LastSampleTimeMs,
        int Samples2Count,
        int? LastSample2Index,
        int? LastSample2TimeMs);

    private sealed record EntListSummary(
        string Path,
        int? Count,
        List<int?>? SampledU03);

    private sealed record EntRecordElemDump(
        string Path,
        int? U03,
        int? U03MinusCountdownMs);

    private readonly record struct SampleCollectionSummary(
        int Count,
        int? LastIndexWithTime,
        int? LastTimeMs);

    private readonly record struct IndexedItem(
        int Index,
        object? Value);

    private readonly record struct ObjectPathChild(
        object Obj,
        string Path);

    private static SampleCollectionSummary GetSampleCollectionSummary(object obj, string collectionPropertyName)
    {
        if (!TryGetMemberValue(obj, collectionPropertyName, out var rawCollection) ||
            rawCollection is null ||
            rawCollection is string ||
            rawCollection is not IEnumerable enumerable)
        {
            return default;
        }

        int count = 0;
        int? lastIndexWithTime = null;
        int? lastTimeMs = null;

        foreach (var sample in enumerable)
        {
            var index = count;
            count++;

            var sampleMs = ExtractSampleTimeMs(sample);
            if (sampleMs.HasValue)
            {
                lastIndexWithTime = index;
                lastTimeMs = sampleMs.Value;
            }

            if (count > 20000)
                break;
        }

        return new SampleCollectionSummary(count, lastIndexWithTime, lastTimeMs);
    }

    private static int? ExtractSampleTimeMs(object? sample)
    {
        if (sample is null)
            return null;

        if (TryGetMemberValue(sample, "Time", out var rawTime) && rawTime is not null)
        {
            var msFromTime = TimeToMs(rawTime);
            if (msFromTime.HasValue)
                return msFromTime.Value;
        }

        var msDirect = TimeToMs(sample);
        if (msDirect.HasValue)
            return msDirect.Value;

        if (TryGetMemberValue(sample, "RaceTime", out var rawRaceTime) && rawRaceTime is not null)
        {
            var msFromRaceTime = TimeToMs(rawRaceTime);
            if (msFromRaceTime.HasValue)
                return msFromRaceTime.Value;
        }

        return null;
    }

    private static int? TryGetIntPropertyValue(object obj, string propertyName)
    {
        if (!TryGetMemberValue(obj, propertyName, out var raw) || raw is null)
            return null;

        if (raw is int i)
            return i;
        if (raw is long l)
            return unchecked((int)l);
        if (raw is uint ui)
            return unchecked((int)ui);

        if (int.TryParse(raw.ToString(), out var parsed))
            return parsed;

        return null;
    }

    private static bool TryGetCollectionCount(object obj, out int count)
    {
        count = 0;

        if (obj is ICollection collection)
        {
            count = collection.Count;
            return true;
        }

        if (!TryGetPropertyValue(obj, "Count", out var raw) || raw is null)
            return false;

        if (raw is int i)
        {
            count = i;
            return true;
        }

        if (raw is long l && l >= 0 && l <= int.MaxValue)
        {
            count = (int)l;
            return true;
        }

        if (raw is uint ui && ui <= int.MaxValue)
        {
            count = (int)ui;
            return true;
        }

        return false;
    }

    private static IEnumerable<IndexedItem> EnumerateCollectionItems(object collectionObj)
    {
        if (TryGetCollectionCount(collectionObj, out var count))
        {
            var itemProperty = collectionObj.GetType().GetProperty(
                "Item",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                returnType: null,
                types: new[] { typeof(int) },
                modifiers: null);

            if (itemProperty is not null && itemProperty.CanRead)
            {
                var limit = Math.Min(count, 20000);
                for (int i = 0; i < limit; i++)
                {
                    object? value = null;
                    try { value = itemProperty.GetValue(collectionObj, new object[] { i }); } catch { }
                    yield return new IndexedItem(i, value);
                }
                yield break;
            }
        }

        if (collectionObj is IEnumerable enumerable)
        {
            int index = 0;
            foreach (var item in enumerable)
            {
                yield return new IndexedItem(index, item);
                index++;
                if (index >= 20000)
                    break;
            }
        }
    }

    private static IEnumerable<ObjectPathChild> EnumerateObjectChildrenWithPaths(object obj, string path)
    {
        if (obj is IEnumerable enumerable && obj is not string)
        {
            int i = 0;
            foreach (var item in enumerable)
            {
                if (item is not null)
                    yield return new ObjectPathChild(item, $"{path}[{i}]");

                i++;
                if (i >= 20000)
                    yield break;
            }
            yield break;
        }

        var type = obj.GetType();

        foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!p.CanRead) continue;
            if (p.GetIndexParameters().Length != 0) continue;

            var pt = p.PropertyType;
            if (pt == typeof(string)) continue;
            if (pt.IsPrimitive || pt.IsEnum) continue;
            if (pt.IsValueType) continue;

            object? value = null;
            try { value = p.GetValue(obj); } catch { }
            if (value is null) continue;

            if (value is IEnumerable childEnumerable && value is not string)
            {
                int i = 0;
                foreach (var item in childEnumerable)
                {
                    if (item is not null)
                        yield return new ObjectPathChild(item, $"{path}.{p.Name}[{i}]");

                    i++;
                    if (i >= 20000)
                        break;
                }
            }
            else
            {
                yield return new ObjectPathChild(value, $"{path}.{p.Name}");
            }
        }

        foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var ft = f.FieldType;
            if (ft == typeof(string)) continue;
            if (ft.IsPrimitive || ft.IsEnum) continue;
            if (ft.IsValueType) continue;

            object? value = null;
            try { value = f.GetValue(obj); } catch { }
            if (value is null) continue;

            if (value is IEnumerable childEnumerable && value is not string)
            {
                int i = 0;
                foreach (var item in childEnumerable)
                {
                    if (item is not null)
                        yield return new ObjectPathChild(item, $"{path}.{f.Name}[{i}]");

                    i++;
                    if (i >= 20000)
                        break;
                }
            }
            else
            {
                yield return new ObjectPathChild(value, $"{path}.{f.Name}");
            }
        }
    }

    private static bool TryGetMemberValue(object obj, string memberName, out object? value)
    {
        if (TryGetPropertyValue(obj, memberName, out value))
            return true;

        value = null;
        try
        {
            var field = obj.GetType().GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is null)
                return false;

            value = field.GetValue(obj);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetPropertyValue(object obj, string propertyName, out object? value)
    {
        value = null;
        try
        {
            var prop = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (prop is null || !prop.CanRead || prop.GetIndexParameters().Length != 0)
                return false;

            value = prop.GetValue(obj);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<T> TraverseForType<T>(object root, int? maxDepth) where T : class
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<(object Obj, int Depth)>();
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            var (obj, depth) = stack.Pop();
            if (obj is null)
                continue;

            if (!visited.Add(obj))
                continue;

            if (obj is T match)
                yield return match;

            if (maxDepth.HasValue && depth >= maxDepth.Value)
                continue;

            foreach (var child in GetChildren(obj))
                stack.Push((child, depth + 1));
        }
    }

    private static IEnumerable<object> GetChildren(object obj)
    {
        if (obj is IEnumerable enumerable && obj is not string)
        {
            int i = 0;
            foreach (var item in enumerable)
            {
                if (item is null) continue;

                yield return item;

                if (++i > 20000) yield break;
            }
            yield break;
        }

        var type = obj.GetType();

        foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!p.CanRead) continue;
            if (p.GetIndexParameters().Length != 0) continue;

            var pt = p.PropertyType;
            if (pt == typeof(string)) continue;
            if (pt.IsPrimitive || pt.IsEnum) continue;
            if (pt.IsValueType) continue;

            object? value = null;
            try { value = p.GetValue(obj); }
            catch {  }

            if (value is not null)
                yield return value;
        }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }

    // ----------------------------
    // Validation removal tag detection
    // ----------------------------

    private sealed record ValidationRemovalTagInfo(
        string? Key,
        string? Note,
        int? AuthorTimeMs,
        string? AuthorTimeSource,
        bool HasSignature);

    private static bool TryGetValidationRemovalTagInfo(
        CScriptTraitsMetadata? metadata,
        out ValidationRemovalTagInfo tagInfo)
    {
        tagInfo = null!;
        if (metadata?.Traits is null || metadata.Traits.Count == 0)
            return false;

        var traits = metadata.Traits.ToList();

        for (int i = traits.Count - 1; i >= 0; i--)
        {
            var kvp = traits[i];
            if (kvp.Value is not CScriptTraitsMetadata.ScriptStructTrait structTrait)
                continue;

            var hasCompressed = TryGetStructFieldText(structTrait, "compressed", out var compressed) &&
                !string.IsNullOrWhiteSpace(compressed);
            var hasSignature = hasCompressed && MatchesValidationRemovalSignature(compressed!);

            var note = TryGetStructFieldText(structTrait, "Note", out var noteText) ? noteText : null;
            int? authorMs = null;
            string? source = null;

            if (TryGetStructFieldStruct(structTrait, "ChallengeParameters", out var cpStruct) && cpStruct is not null)
            {
                if (TryGetStructFieldInt(cpStruct, "AuthorTime", out var cpAuthorMs) && cpAuthorMs >= 0)
                {
                    authorMs = cpAuthorMs;
                    source = "ChallengeParameters.AuthorTime";
                }
            }

            if (!authorMs.HasValue &&
                TryExtractAuthorTimeFromRemovalNote(note, out var noteAuthorMs, out var noteToken))
            {
                authorMs = noteAuthorMs;
                source = $"Note.AuthorTime ({noteToken})";
            }

            var noteLooksLikeRemoval = !string.IsNullOrWhiteSpace(note) &&
                (note!.IndexOf("AuthorTime=", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 note.IndexOf("RaceValidationReplay", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 note.IndexOf("ValidationReplay Remover", StringComparison.OrdinalIgnoreCase) >= 0);
            var looksLikeRemovalStruct = hasCompressed || noteLooksLikeRemoval;

            if (!hasSignature && !looksLikeRemovalStruct)
                continue;

            if (!hasSignature && !authorMs.HasValue)
                continue;

            tagInfo = new ValidationRemovalTagInfo(kvp.Key, note, authorMs, source, hasSignature);
            return true;
        }

        for (int i = traits.Count - 1; i >= 0; i--)
        {
            var kvp = traits[i];
            if (kvp.Value is null)
                continue;

            if (TraitContainsValidationRemovalTag(kvp.Value))
            {
                tagInfo = new ValidationRemovalTagInfo(kvp.Key, null, null, null, true);
                return true;
            }
        }

        return false;
    }

    private static string BuildValidationTagNote(ValidationRemovalTagInfo info, string baseNote)
    {
        var keyPart = string.IsNullOrWhiteSpace(info.Key) ? null : $"tagKey={info.Key}";
        var sourcePart = string.IsNullOrWhiteSpace(info.AuthorTimeSource) ? null : $"tagAuthorTimeSource={info.AuthorTimeSource}";
        var signaturePart = info.HasSignature ? "removalSignature=present" : "removalSignature=missing";
        return JoinNonEmpty(baseNote, keyPart, sourcePart, signaturePart);
    }

    private static bool TryGetStructField(
        CScriptTraitsMetadata.ScriptStructTrait structTrait,
        string fieldName,
        out CScriptTraitsMetadata.ScriptTrait? trait)
    {
        trait = null;
        if (structTrait.Value is null)
            return false;

        if (!structTrait.Value.TryGetValue(fieldName, out var t) || t is null)
            return false;

        trait = t;
        return true;
    }

    private static bool TryGetStructFieldStruct(
        CScriptTraitsMetadata.ScriptStructTrait structTrait,
        string fieldName,
        out CScriptTraitsMetadata.ScriptStructTrait? childStruct)
    {
        childStruct = null;
        if (!TryGetStructField(structTrait, fieldName, out var trait))
            return false;

        if (trait is CScriptTraitsMetadata.ScriptStructTrait st)
        {
            childStruct = st;
            return true;
        }

        return false;
    }

    private static bool TryGetStructFieldText(
        CScriptTraitsMetadata.ScriptStructTrait structTrait,
        string fieldName,
        out string? value)
    {
        value = null;
        if (!TryGetStructField(structTrait, fieldName, out var trait) || trait is null)
            return false;

        object? raw = null;
        try { raw = trait.GetValue(); } catch { }

        if (raw is string s)
        {
            value = s;
            return true;
        }

        if (raw is not null)
        {
            value = raw.ToString();
            return !string.IsNullOrWhiteSpace(value);
        }

        return false;
    }

    private static bool TryGetStructFieldInt(
        CScriptTraitsMetadata.ScriptStructTrait structTrait,
        string fieldName,
        out int value)
    {
        value = default;
        if (!TryGetStructField(structTrait, fieldName, out var trait) || trait is null)
            return false;

        object? raw = null;
        try { raw = trait.GetValue(); } catch { }

        if (raw is int i)
        {
            value = i;
            return true;
        }

        if (raw is long l)
        {
            value = unchecked((int)l);
            return true;
        }

        if (raw is uint ui)
        {
            value = unchecked((int)ui);
            return true;
        }

        if (raw is not null && int.TryParse(raw.ToString(), out var p))
        {
            value = p;
            return true;
        }

        return false;
    }

    private static bool TryExtractAuthorTimeFromRemovalNote(
        string? note,
        out int authorTimeMs,
        out string? token)
    {
        authorTimeMs = default;
        token = null;

        if (string.IsNullOrWhiteSpace(note))
            return false;

        var idx = note.IndexOf("AuthorTime=", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return false;

        idx += "AuthorTime=".Length;
        var end = note.IndexOf(';', idx);
        var raw = (end >= 0 ? note.Substring(idx, end - idx) : note.Substring(idx)).Trim();

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        token = raw;

        if (!TryParseTmTimeToMs(raw, out var ms))
            return false;

        authorTimeMs = ms;
        return true;
    }

    private static bool TraitContainsValidationRemovalTag(CScriptTraitsMetadata.ScriptTrait trait)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<object>();
        stack.Push(trait);

        while (stack.Count > 0)
        {
            var obj = stack.Pop();
            if (obj is null)
                continue;

            if (obj is string s)
            {
                if (MatchesValidationRemovalSignature(s))
                    return true;
                continue;
            }

            if (!visited.Add(obj))
                continue;

            if (obj is CScriptTraitsMetadata.ScriptTrait st)
            {
                object? value = null;
                try { value = st.GetValue(); } catch { }
                if (value is not null && !ReferenceEquals(value, obj))
                    stack.Push(value);

                if (st is CScriptTraitsMetadata.ScriptStructTrait structTrait && structTrait.Value is not null)
                {
                    foreach (var child in structTrait.Value.Values)
                    {
                        if (child is not null)
                            stack.Push(child);
                    }
                }
                else if (st is CScriptTraitsMetadata.ScriptArrayTrait arrayTrait && arrayTrait.Value is not null)
                {
                    foreach (var child in arrayTrait.Value)
                    {
                        if (child is not null)
                            stack.Push(child);
                    }
                }
            }

            if (obj is IDictionary dict)
            {
                foreach (DictionaryEntry entry in dict)
                {
                    if (entry.Key is not null)
                        stack.Push(entry.Key);
                    if (entry.Value is not null)
                        stack.Push(entry.Value);
                }
                continue;
            }

            if (obj is IEnumerable enumerable && obj is not string)
            {
                int i = 0;
                foreach (var item in enumerable)
                {
                    if (item is not null)
                        stack.Push(item);
                    if (++i > 20000)
                        break;
                }
            }
        }

        return false;
    }

    private static bool MatchesValidationRemovalSignature(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var s = value.Trim();

        if (string.Equals(s, ValidationRemovalSignatureText, StringComparison.Ordinal))
            return true;

        if (string.Equals(s, ValidationRemovalSignatureHex, StringComparison.OrdinalIgnoreCase))
            return true;

        if (TryDecodeHexToAscii(s, out var decoded) &&
            string.Equals(decoded, ValidationRemovalSignatureText, StringComparison.Ordinal))
            return true;

        return false;
    }

    private static bool TryDecodeHexToAscii(string hex, out string decoded)
    {
        decoded = string.Empty;
        if (string.IsNullOrWhiteSpace(hex))
            return false;

        var s = hex.Trim();
        if (s.Length % 2 != 0)
            return false;

        var byteCount = s.Length / 2;
        Span<byte> bytes = byteCount <= 512 ? stackalloc byte[byteCount] : new byte[byteCount];

        for (int i = 0; i < byteCount; i++)
        {
            var slice = s.AsSpan(i * 2, 2);
            if (!byte.TryParse(slice, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                return false;
            bytes[i] = b;
        }

        decoded = Encoding.ASCII.GetString(bytes);
        return true;
    }

    private static string BuildSignatureHexString(string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
            sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    // ----------------------------
    // Script metadata extraction
    // ----------------------------

    private static List<int>? ExtractWaypointTimes(CScriptTraitsMetadata? metadata)
    {
        if (metadata?.Traits is null)
            return null;

        if (!metadata.Traits.TryGetValue(WaypointTimesKey, out var trait))
            return null;

        if (trait is CScriptTraitsMetadata.ScriptArrayTrait arr)
        {
            var list = new List<int>(arr.Value.Count);
            foreach (var el in arr.Value)
            {
                var v = el.GetValue();
                if (v is int i) list.Add(i);
                else if (v is long l) list.Add(unchecked((int)l));
                else if (v is not null && int.TryParse(v.ToString(), out var p)) list.Add(p);
            }
            return list;
        }

        var value = trait.GetValue();
        if (value is IEnumerable enumerable && value is not string)
        {
            var list = new List<int>();
            foreach (var item in enumerable)
            {
                if (item is CScriptTraitsMetadata.ScriptTrait st)
                {
                    var v = st.GetValue();
                    if (v is int i) list.Add(i);
                    else if (v is long l) list.Add(unchecked((int)l));
                    else if (v is not null && int.TryParse(v.ToString(), out var p)) list.Add(p);
                }
            }
            return list;
        }

        return null;
    }

    // ----------------------------
    // Time parsing helpers
    // ----------------------------

    private static int? TimeToMs(object? timeObj)
    {
        if (timeObj is null)
            return null;

        var t = timeObj.GetType();

        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            var hasValue = (bool)(t.GetProperty("HasValue")?.GetValue(timeObj) ?? false);
            if (!hasValue) return null;

            timeObj = t.GetProperty("Value")?.GetValue(timeObj);
            if (timeObj is null) return null;

            t = timeObj.GetType();
        }

        if (timeObj is int i) return i;
        if (timeObj is long l) return checked((int)l);
        if (timeObj is uint ui) return unchecked((int)ui);
        if (timeObj is TimeSpan ts) return (int)Math.Round(ts.TotalMilliseconds);

        object? candidate =
            t.GetProperty("TotalMilliseconds")?.GetValue(timeObj) ??
            t.GetProperty("Milliseconds")?.GetValue(timeObj) ??
            t.GetProperty("Value")?.GetValue(timeObj);

        if (candidate is not null)
        {
            if (!ReferenceEquals(candidate, timeObj))
            {
                var inner = TimeToMs(candidate);
                if (inner.HasValue) return inner.Value;
            }

            if (candidate is double d) return (int)Math.Round(d);
            if (candidate is float f) return (int)Math.Round(f);
            if (int.TryParse(candidate.ToString(), out var pi)) return pi;
        }

        if (TryParseTmTimeToMs(timeObj.ToString(), out var ms))
            return ms;

        return null;
    }

    private static bool TryParseTmTimeToMs(string? s, out int ms)
    {
        ms = 0;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        s = s.Trim();

        // Expected formats:
        //   m:ss.mmm   e.g. "1:03.502"
        //   h:mm:ss.mmm
        //   ss.mmm
        var parts = s.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        int hours = 0;
        int minutes = 0;
        string secPart;

        if (parts.Length == 3)
        {
            if (!int.TryParse(parts[0], out hours)) return false;
            if (!int.TryParse(parts[1], out minutes)) return false;
            secPart = parts[2];
        }
        else if (parts.Length == 2)
        {
            if (!int.TryParse(parts[0], out minutes)) return false;
            secPart = parts[1];
        }
        else if (parts.Length == 1)
        {
            secPart = parts[0];
        }
        else
        {
            return false;
        }

        int seconds;
        int millis = 0;

        var secMillis = secPart.Split('.', StringSplitOptions.TrimEntries);
        if (!int.TryParse(secMillis[0], out seconds)) return false;

        if (secMillis.Length > 1)
        {
            var msStr = secMillis[1];
            if (msStr.Length > 3) msStr = msStr.Substring(0, 3);
            if (msStr.Length < 3) msStr = msStr.PadRight(3, '0');
            if (!int.TryParse(msStr, out millis)) return false;
        }

        long total = (long)hours * 3600000L + (long)minutes * 60000L + (long)seconds * 1000L + millis;
        if (total < 0 || total > int.MaxValue) return false;

        ms = (int)total;
        return true;
    }

    // ----------------------------
    // File helpers
    // ----------------------------

    private static bool LooksLikeGbx(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> b = stackalloc byte[3];
            var read = fs.Read(b);
            return read == 3 && b[0] == 0x47 && b[1] == 0x42 && b[2] == 0x58; // "GBX"
        }
        catch
        {
            return false;
        }
    }

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

    private sealed record ManualEntry(bool Valid, string? Note);

    private sealed record ReplayEntry(string Path, HashSet<int> GhostTimesMs);

    private sealed class Report
    {
        public string? Uid { get; set; }
        public string? Validated { get; set; }
        public string? Type { get; set; }
        public string? Note { get; set; }
        public GpsValidationDetails? GpsValidation { get; set; }
        public string? Path { get; set; }
        public string? MapName { get; set; }
        public string? ReplayPath { get; set; }
        public string? Error { get; set; }
        public DataDump? DataDump { get; set; }
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
