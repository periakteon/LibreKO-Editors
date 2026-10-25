using System.Text.Json.Nodes;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Events;
using LibreKO.Editors.DataHost.Events;
using Microsoft.EntityFrameworkCore;
internal static class EventFixture {
 internal const string Cs="Server=127.0.0.1;Port=33307;Database=libreko_editor_test_native;User=root;Pooling=false;";
 static int id=Random.Shared.Next(10000000,100000000);internal static int Id()=>Interlocked.Increment(ref id);
 internal static AppDbContext Open()=>new(new DbContextOptionsBuilder<AppDbContext>().UseMySql(Cs,new MySqlServerVersion(new Version(8,2,0))).Options);
 internal static async Task Ensure(){await FixtureRows.EnsureItem();await using var db=Open();if(!await db.ZoneInfos.AnyAsync(z=>z.ZoneNo==210))db.ZoneInfos.Add(new ZoneInfoData{ZoneNo=210,MapName="Event test zone"});if(!await db.Npcs.AnyAsync(n=>n.Id==880001))db.Npcs.Add(new NpcData{Id=880001,Name="Event test NPC"});await db.SaveChangesAsync();}
 internal static JsonObject Row(EventTable table,int id,int? zone=null){var row=(JsonObject)EventSchema.Catalog().Tables.Single(t=>t.Table==table).Defaults.DeepClone();row[EventKeys.Fields(table)[^1]]=id;if(zone!=null)row["ZoneNum"]=zone.Value;return row;}
 internal static EventDraft New(EventFamily family,int id,int? zone=null)=>new(new(new(family,id,zone),[],"missing"));
 internal static void Add(EventDraft d,EventTable table,JsonObject row)=>d.Add(EventKeys.From(table,row),row);
 internal static EventDraft Race(int id){var d=New(EventFamily.CollectionRace,id);var row=Row(EventTable.CollectionRaces,id);row["Name"]="Test race";row["ZoneId"]=210;Add(d,EventTable.CollectionRaces,row);foreach(var table in new[]{EventTable.CollectionRaceSchedules,EventTable.CollectionRaceObjectives,EventTable.CollectionRaceRewards}){var child=Row(table,Id());child["RaceId"]=id;if(table==EventTable.CollectionRaceObjectives){child["Kind"]=0;child["TargetId"]=880001;child["Count"]=2;}if(table==EventTable.CollectionRaceRewards){child["ItemId"]=123450001;child["ItemCount"]=1;}Add(d,table,child);}return d;}
}
internal sealed class TestEventJournal : IEventJournal {
 internal int Prepared,Committed,Aborted;internal bool FailPrepare,FailCommit;
 public void Prepare(EventChange c){if(FailPrepare)throw new IOException("prepare failure");Prepared++;}
 public void Commit(EventChange c,EventDocumentSnapshot? after){if(FailCommit)throw new IOException("commit outcome failure");Committed++;}
 public void Abort(Guid id)=>Aborted++;
}
