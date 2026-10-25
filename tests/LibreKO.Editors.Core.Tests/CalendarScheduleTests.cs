using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Events;
using System.Text.Json.Nodes;

public class CalendarScheduleTests
{
    [Fact] public void CalendarDatesAreStrictAndHaveNoTimezone()
    {
        var field=new FieldDescriptor("Date","Schedule",FieldKind.DateOnly,true);
        Assert.Equal("2026-10-25",FieldValueParser.Parse(field,"2026-10-25")!.GetValue<string>());
        Assert.Null(FieldValueParser.Parse(field,""));
        Assert.Throws<FormatException>(()=>FieldValueParser.Parse(field,"2026-02-30"));
        Assert.Throws<FormatException>(()=>FieldValueParser.Parse(field,"2026-10-25T23:00:00Z"));
    }
    [Theory]
    [InlineData(2,"2026-10-25",0,"2026-10-25")]
    [InlineData(3,null,31,"31")]
    [InlineData(99,null,0,"99")]
    public void CalendarRulesAreNotDisplayedAsEveryWeekday(int repeat,string? date,int dayOfMonth,string label)
    {
        var row=new EventRowSnapshot(new(EventTable.LotterySchedules,1),new(){["Repeat"]=repeat,["Date"]=date,["Day"]=1,["DayOfMonth"]=dayOfMonth,["Hour"]=7,["Minute"]=30},"v");
        var entry=Assert.Single(EventScheduleProjection.Project(new(new(EventFamily.Lottery,1),[row],"v"),null));
        Assert.NotNull(entry.CalendarRule);Assert.Contains(label,entry.CalendarRule);Assert.Equal("UTC",entry.Clock);
    }
    [Fact] public void DailyRulesIgnoreAnOldWeekdayAndWeeklyRulesRetainIt()
    {
        EventScheduleEntry Project(int repeat)=>Assert.Single(EventScheduleProjection.Project(new(new(EventFamily.Temple,1),[new(new(EventTable.TempleEventSchedules,1),new(){["Repeat"]=repeat,["Day"]=2,["Hour"]=7,["Minute"]=30},"v")],"v"),null));
        Assert.Null(Project(0).Day);Assert.Equal(DayOfWeek.Tuesday,Project(1).Day);
    }
    [Fact] public void WeeklyRuleWithoutAWeekdayIsNotShownAsDaily()
    {
        var row=new EventRowSnapshot(new(EventTable.LotterySchedules,1),new(){["Repeat"]=1,["Day"]=null,["Hour"]=7,["Minute"]=30},"v");
        var entry=Assert.Single(EventScheduleProjection.Project(new(new(EventFamily.Lottery,1),[row],"v"),null));
        Assert.NotNull(entry.CalendarRule);
    }
}
