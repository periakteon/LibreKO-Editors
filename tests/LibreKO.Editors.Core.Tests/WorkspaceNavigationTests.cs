using LibreKO.Editors.Core;
using LibreKO.Editors.Contracts;
using System.Text.Json;
public class WorkspaceNavigationTests {
 [Fact] public async Task SaveFailureAndCancelKeepCurrentWorkspace(){int saves=0,discards=0;Task<bool> Save(){saves++;return Task.FromResult(false);}Assert.False(await WorkspaceNavigation.CanLeaveAsync(true,LeaveDecision.Cancel,Save,()=>discards++));Assert.False(await WorkspaceNavigation.CanLeaveAsync(true,LeaveDecision.Save,Save,()=>discards++));Assert.Equal(1,saves);Assert.Equal(0,discards);Assert.True(await WorkspaceNavigation.CanLeaveAsync(true,LeaveDecision.Discard,Save,()=>discards++));Assert.Equal(1,discards);Assert.True(await WorkspaceNavigation.CanLeaveAsync(true,LeaveDecision.Save,()=>Task.FromResult(true),()=>discards++));}
 [Fact] public void OldProfileLoadsWithoutServerTimezone(){var profile=JsonSerializer.Deserialize<WorkspaceProfile>("{\"assetRoot\":\"assets\",\"workspaceRoot\":\"workspace\"}",JsonConfig.Options)!;Assert.Null(profile.ServerScheduleTimeZone);Assert.DoesNotContain("password",JsonSerializer.Serialize(profile,JsonConfig.Options));}
}
