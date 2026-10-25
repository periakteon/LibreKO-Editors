using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Events;
using LibreKO.Editors.DataHost.Events;
using System.Text.Json.Nodes;

public class CalendarScheduleDatabaseTests
{
    [Fact] public void ScheduleDateMetadataIsCalendarTyped()
    {
        foreach(var table in new[]{EventTable.TempleEventSchedules,EventTable.CollectionRaceSchedules,EventTable.LotterySchedules})
            Assert.Equal("DateOnly",EventSchema.Catalog().Tables.Single(t=>t.Table==table).Fields.Single(f=>f.Field.Name=="Date").Field.Kind.ToString());
    }
    [Theory]
    [InlineData(1,null,null,0,false)]
    [InlineData(2,null,null,0,false)]
    [InlineData(2,"2026-10-25",null,0,true)]
    [InlineData(2,"2026-02-30",null,0,false)]
    [InlineData(3,null,null,0,false)]
    [InlineData(3,null,null,31,true)]
    public void RepeatRequirementsMatchTheCurrentServer(int repeat,string? date,int? day,int dayOfMonth,bool valid)
    {
        var values=EventFixture.Row(EventTable.TempleEventSchedules,123);values["Event"]=1;values["Repeat"]=repeat;values["Date"]=date;values["Day"]=day;values["DayOfMonth"]=dayOfMonth;
        var change=new EventChange(Guid.NewGuid(),new(new(EventFamily.Temple,1),[],"missing"),[new(new(EventTable.TempleEventSchedules,123),null,values)]);
        Assert.Equal(valid,EventRules.Validate(change,EventSchema.Catalog()).Count==0);
    }
    [Fact] public async Task OneTimeDateSavesReadsAndReversesThroughGuardedHistory()
    {
        await EventFixture.Ensure();var service=new EventDatabase(EventFixture.Cs,Path.Combine(Path.GetTempPath(),"calendar-"+Guid.NewGuid()));
        var draft=EventFixture.Race(EventFixture.Id());var schedule=draft.Rows.Single(r=>r.Key.Table==EventTable.CollectionRaceSchedules).Key;
        draft.Set(schedule,"Repeat",JsonValue.Create(2));draft.Set(schedule,"Date",JsonValue.Create("2026-10-25"));
        var change=draft.BuildChange(Guid.NewGuid());var result=await service.SaveAsync(change);
        Assert.True(result.Status==SaveStatus.Saved,result.Message+string.Join(";",result.Issues?.Select(i=>i.Field+": "+i.Message)??[]));
        var saved=(await service.GetAsync(draft.Original.Key))!;Assert.Equal("2026-10-25",saved.Rows.Single(r=>r.Key==schedule).Values["Date"]!.GetValue<string>());
        draft=new(saved);draft.Set(schedule,"Date",JsonValue.Create("2026-10-26"));var edit=draft.BuildChange(Guid.NewGuid());
        Assert.Equal(SaveStatus.Saved,(await service.SaveAsync(edit)).Status);Assert.Equal(SaveStatus.Saved,(await service.ReplayAsync(edit.OperationId,true)).Status);
        Assert.Equal("2026-10-25",(await service.GetAsync(saved.Key))!.Rows.Single(r=>r.Key==schedule).Values["Date"]!.GetValue<string>());
    }
}
