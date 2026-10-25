using System.Text.Json.Nodes;
using LibreKO.Editors.Core;
using LibreKO.Editors.Contracts;
public class DraftTests {
 [Fact]public void EditedIntegerCanBeReadByPreviewAndReferenceContext(){var field=new FieldDescriptor("MapId","Position",FieldKind.Integer,true,0,255);var value=FieldValueParser.Parse(field,"21");Assert.Equal(21,FieldValueParser.ReadInt32(value));Assert.Equal(-2147483648,FieldValueParser.ReadInt32(System.Text.Json.Nodes.JsonValue.Create(int.MinValue)));}
 public static RecordSnapshot Row()=>new(new(RecordKind.Item,1),new(){["Name"]="Original",["Damage"]=10},"v");
 [Fact]public void UndoRedoProducesOnlyChangedFields(){var d=new RecordDraft(Row());d.Set("Damage",20);d.Undo();Assert.Empty(d.BuildChange(Guid.NewGuid()).ChangedValues);d.Redo();Assert.Equal(20,d.BuildChange(Guid.NewGuid()).ChangedValues["Damage"]!.GetValue<int>());Assert.Single(d.BuildChange(Guid.NewGuid()).ChangedValues);}
 [Fact]public void HashIsIndependentOfPropertyOrder(){Assert.Equal(RecordFingerprint.Compute(new(){["a"]=1,["b"]=2}),RecordFingerprint.Compute(new(){["b"]=2,["a"]=1}));}
 [Fact]public void RecoveryDoesNotReapplyCommittedValues(){var before=Row();var c=new RecordChange(Guid.NewGuid(),before,new(){["Damage"]=20});Assert.Equal(RecoveryDisposition.Applied,RecoveryResolver.Resolve(c,before with{Values=new(){["Damage"]=20,["Name"]="Original"}}));Assert.Equal(RecoveryDisposition.Conflict,RecoveryResolver.Resolve(c,before with{Values=new(){["Damage"]=30}}));}
 [Fact]public void IntegerParsingIsExactAndRangeChecked(){var f=new FieldDescriptor("Experience","Stats",FieldKind.Integer,true,long.MinValue,long.MaxValue);Assert.Equal(9007199254740993L,FieldValueParser.Parse(f,"9007199254740993")!.GetValue<long>());Assert.Throws<FormatException>(()=>FieldValueParser.Parse(f with{Maximum=255},"256"));}
}
