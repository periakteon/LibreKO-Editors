using LibreKO.Editors.Core;
using System.Text.Json.Nodes;

public class BrowserHistoryTests
{
    static WorkspaceLocation At(int id) => new("world", new JsonObject { ["id"] = id });
    [Fact] public void CancelDoesNotMoveAndNewNavigationClearsForward()
    {
        var history = new BrowserHistory(); history.Reset("db");
        history.Visit(At(1)); history.Visit(At(2)); history.Visit(At(3));
        var previous = history.Back;
        Assert.Equal(2, previous!.State["id"]!.GetValue<int>());
        Assert.Equal(3, history.Current!.State["id"]!.GetValue<int>());
        Assert.True(history.CommitMove(previous));
        Assert.Equal(3, history.Forward!.State["id"]!.GetValue<int>());
        history.Visit(At(4)); Assert.Null(history.Forward);
        Assert.Equal(2, history.Back!.State["id"]!.GetValue<int>());
    }
    [Fact] public void LocationsAreIsolatedBoundedAndScoped()
    {
        var history = new BrowserHistory(3); history.Reset("a");
        var location = At(1); history.Visit(location); location.State["id"] = 99;
        var captured = history.Current!; captured.State["id"] = 98;
        Assert.Equal(1, history.Current!.State["id"]!.GetValue<int>());
        history.Visit(At(2)); history.Visit(At(3)); history.Visit(At(4));
        Assert.True(history.CommitMove(history.Back!)); Assert.True(history.CommitMove(history.Back!)); Assert.Null(history.Back);
        Assert.False(history.CommitMove(At(99)));
        history.Reset("b"); Assert.Null(history.Current); Assert.Null(history.Back); Assert.Null(history.Forward);
    }
    [Fact] public void SameLocationUpdatesWithoutDuplicateAndRefreshCannotJumpCursor()
    {
        var history = new BrowserHistory(); history.Reset("db"); history.Visit(At(1)); history.Visit(At(1)); Assert.Null(history.Back);
        history.Visit(At(2)); var stale = history.Back!; history.Reset("other"); history.Visit(At(3)); Assert.False(history.CommitMove(stale));
    }
    [Fact] public void RepeatedDestinationsDoNotConfuseForwardWithBack()
    {
        var history=new BrowserHistory();history.Reset("db");history.Visit(At(1));history.Visit(At(2));history.Visit(At(1));
        Assert.True(history.CommitMove(history.Back!));
        Assert.True(history.CommitMove(history.Forward!,true));
        Assert.NotNull(history.Back);Assert.Equal(2,history.Back.State["id"]!.GetValue<int>());Assert.Null(history.Forward);
    }
}
