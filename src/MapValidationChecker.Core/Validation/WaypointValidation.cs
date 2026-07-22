namespace MapValidationChecker.Core.Validation;

public static class WaypointValidation
{
    public static int GetExpectedWaypointCount(MapCheckpointFacts checkpoints)
    {
        ArgumentNullException.ThrowIfNull(checkpoints);
        return checkpoints.NbCheckpoints * GetLapMultiplier(checkpoints);
    }

    public static bool CountLooksPlausible(
        MapCheckpointFacts checkpoints,
        int metadataWaypointCount)
    {
        ArgumentNullException.ThrowIfNull(checkpoints);

        var expectedWaypointCount = GetExpectedWaypointCount(checkpoints);
        if (metadataWaypointCount == expectedWaypointCount)
            return true;

        if (checkpoints.InvalidLinkedCheckpointGroupCount <= 0)
            return false;

        var adjustedExpectedWaypointCount = expectedWaypointCount -
            (checkpoints.InvalidLinkedCheckpointGroupCount * GetLapMultiplier(checkpoints));

        return metadataWaypointCount == adjustedExpectedWaypointCount;
    }

    private static int GetLapMultiplier(MapCheckpointFacts checkpoints) =>
        checkpoints.IsLapRace ? Math.Max(checkpoints.NbLaps, 1) : 1;
}
