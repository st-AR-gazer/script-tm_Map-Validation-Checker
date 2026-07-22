using MapValidationChecker.Core.Validation;

namespace MapValidationChecker.Core.Tests;

public sealed class WaypointValidationTests
{
    [Theory]
    [InlineData(4, false, 0, 4)]
    [InlineData(4, false, 5, 4)]
    [InlineData(4, true, 0, 4)]
    [InlineData(4, true, 1, 4)]
    [InlineData(4, true, 3, 12)]
    public void Expected_count_uses_laps_only_for_lap_races(
        int checkpointCount,
        bool isLapRace,
        int lapCount,
        int expected)
    {
        var facts = new MapCheckpointFacts(
            checkpointCount,
            isLapRace,
            lapCount,
            InvalidLinkedCheckpointGroupCount: 0);

        Assert.Equal(expected, WaypointValidation.GetExpectedWaypointCount(facts));
    }

    [Theory]
    [InlineData(8, true)]
    [InlineData(6, true)]
    [InlineData(7, false)]
    [InlineData(5, false)]
    public void Invalid_linked_group_allows_only_the_exact_per_lap_shortfall(
        int metadataCount,
        bool expected)
    {
        var facts = new MapCheckpointFacts(
            NbCheckpoints: 4,
            IsLapRace: true,
            NbLaps: 2,
            InvalidLinkedCheckpointGroupCount: 1);

        Assert.Equal(expected, WaypointValidation.CountLooksPlausible(facts, metadataCount));
    }
}
