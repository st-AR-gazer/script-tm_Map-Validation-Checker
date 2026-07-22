using System.Collections;

using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Script;

using MapValidationChecker.Core.Validation;

namespace MapValidationChecker.Cli.Evidence;

internal static class WaypointMetadataReader
{
    private const string WaypointTimesKey = "Race_AuthorRaceWaypointTimes";
    private const string LinkedCheckpointTag = "LinkedCheckpoint";

    public static WaypointEvidence Read(CGameCtnChallenge map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var times = ReadTimes(map.ScriptMetadata);
        if (times is not { Count: > 0 })
            return new WaypointEvidence(null, InvalidLinkedCheckpointGroupCount: 0);

        return new WaypointEvidence(
            new WaypointMetadataEvidence(times[^1], times.Count),
            GetInvalidLinkedCheckpointOrders(map).Count);
    }

    public static List<int>? ReadTimes(CScriptTraitsMetadata? metadata)
    {
        if (metadata?.Traits is null ||
            !metadata.Traits.TryGetValue(WaypointTimesKey, out var trait))
        {
            return null;
        }

        if (trait is CScriptTraitsMetadata.ScriptArrayTrait arrayTrait)
        {
            var times = new List<int>(arrayTrait.Value.Count);
            foreach (var element in arrayTrait.Value)
                AddTime(element.GetValue(), times);
            return times;
        }

        var value = trait.GetValue();
        if (value is not IEnumerable enumerable || value is string)
            return null;

        var enumerableTimes = new List<int>();
        foreach (var item in enumerable)
        {
            if (item is CScriptTraitsMetadata.ScriptTrait scriptTrait)
                AddTime(scriptTrait.GetValue(), enumerableTimes);
        }

        return enumerableTimes;
    }

    public static List<int> GetInvalidLinkedCheckpointOrders(CGameCtnChallenge map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return EnumerateWaypointSpecialProperties(map)
            .Where(IsInvalidLinkedCheckpoint)
            .Select(waypoint => waypoint.Order)
            .Distinct()
            .OrderBy(order => order)
            .ToList();
    }

    private static void AddTime(object? value, ICollection<int> times)
    {
        if (value is int intValue)
            times.Add(intValue);
        else if (value is long longValue)
            times.Add(unchecked((int)longValue));
        else if (value is not null && int.TryParse(value.ToString(), out var parsedValue))
            times.Add(parsedValue);
    }

    private static IEnumerable<CGameWaypointSpecialProperty> EnumerateWaypointSpecialProperties(
        CGameCtnChallenge map)
    {
        foreach (var block in map.GetBlocks())
        {
            if (block.WaypointSpecialProperty is not null)
                yield return block.WaypointSpecialProperty;
        }

        foreach (var item in map.GetAnchoredObjects())
        {
            if (item.WaypointSpecialProperty is not null)
                yield return item.WaypointSpecialProperty;
        }
    }

    private static bool IsInvalidLinkedCheckpoint(CGameWaypointSpecialProperty waypoint) =>
        string.Equals(waypoint.Tag, LinkedCheckpointTag, StringComparison.OrdinalIgnoreCase) &&
        waypoint.Order == -1;
}

internal sealed record WaypointEvidence(
    WaypointMetadataEvidence? Metadata,
    int InvalidLinkedCheckpointGroupCount);
