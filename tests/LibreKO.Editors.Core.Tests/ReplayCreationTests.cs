using System.Text.Json.Nodes;
using LibreKO.Editors.Core;
using LibreKO.Editors.Contracts;
public class ReplayCreationTests{
 [Fact]public void MissingCreationCanBeReconciledWithoutReapplying(){var c=new RecordChange(Guid.NewGuid(),new(new(RecordKind.Item,90),new(),"missing"),new(){["Num"]=90,["Name"]="New"},Create:true);Assert.Equal(RecoveryDisposition.NotApplied,RecoveryResolver.Resolve(c,null!));}
 [Fact]public void MissingEditedRowRequiresConflictResolution(){var c=new RecordChange(Guid.NewGuid(),new(new(RecordKind.Item,90),new(){["Name"]="Old"},"v"),new(){["Name"]="New"});Assert.Equal(RecoveryDisposition.Conflict,RecoveryResolver.Resolve(c,null!));}
 [Fact]public void CreationCanBeReplayedIntoMissingRow(){var c=new RecordChange(Guid.NewGuid(),new(new(RecordKind.Item,90),new(),"missing"),new(){["Num"]=90,["Name"]="New"},Create:true);var replay=ChangeReplay.Prepare(c,null,new(new(RecordKind.Item,90),c.ChangedValues,"saved"),false);Assert.True(replay.Create);Assert.NotEqual(c.OperationId,replay.OperationId);}
 [Fact]public void CreationReplayCannotOverwriteExistingRow(){var c=new RecordChange(Guid.NewGuid(),new(new(RecordKind.Item,90),new(),"missing"),new(){["Num"]=90,["Name"]="New"},Create:true);var row=new RecordSnapshot(new(RecordKind.Item,90),new(){["Name"]="Someone else's"},"v");Assert.Throws<InvalidOperationException>(()=>ChangeReplay.Prepare(c,row,row,false));}
}
