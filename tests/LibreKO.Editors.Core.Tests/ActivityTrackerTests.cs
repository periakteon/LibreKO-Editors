using LibreKO.Editors.Core;
public class ActivityTrackerTests
{
    [Fact] public void OverlappingLocalActivitiesKeepGlobalBusyUntilLastDispose()
    {
        var tracker = new ActivityTracker();
        var first = tracker.Begin("map", "Loading terrain"); var second = tracker.Begin("related", "Loading placements");
        Assert.True(tracker.IsBusy()); Assert.True(tracker.IsBusy("map")); Assert.False(tracker.IsBusy("missing"));
        first.Dispose(); first.Dispose(); Assert.True(tracker.IsBusy()); Assert.False(tracker.IsBusy("map"));
        Assert.Equal(new[] { "Loading placements" }, tracker.Messages()); second.Dispose(); Assert.False(tracker.IsBusy());
    }
    [Fact] public async Task FailureAndCancellationReleaseTheActivity()
    {
        var tracker = new ActivityTracker();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => { using var lease = tracker.Begin("source", "Building preview"); await Task.Yield(); throw new OperationCanceledException(); });
        Assert.False(tracker.IsBusy()); Assert.Empty(tracker.Messages());
    }
}
