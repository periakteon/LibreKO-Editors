using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
public class CompatibilityTests {
 [Theory][InlineData("{\"tex\":[\"particle\"]}","particle")][InlineData("{\"texs\":[\"mesh\"]}","mesh")][InlineData("{\"tex\":\"single\"}","single")]
 public void AuditsAllRendererEffectTextureFormats(string json,string stem){var refs=CompatibilityResources.References("assets/fx/mesh/test.json",System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject());Assert.Contains(refs,r=>r.Path==$"res://assets/fx/tex/{stem}.png");}
 [Theory][InlineData(1000,2,1002)][InlineData(1001,2,null)][InlineData(2147483000,999,null)][InlineData(1000,-2,null)]
 public void VariantsRequireARealBaseAndSafeArithmetic(int id,int extension,int? expected)=>Assert.Equal(expected,CompatibilityRules.VariantId(id,extension));
 [Fact] public void EveryArmorRaceFallbackGroupIsChecked(){var report=new CompatibilityReportBuilder();var audit=new CompatibilityResourceAudit(p=>p.EndsWith("race1.glb"),report);Assert.Equal("res://assets/armor/race1.glb",audit.Resolve(["res://assets/armor/prefix1.glb","res://assets/armor/race1.glb"],"Race 1"));Assert.Null(audit.Resolve(["res://assets/armor/prefix2.glb","res://assets/armor/race2.glb"],"Race 2"));Assert.Contains(report.Examples,i=>i.Code=="asset.missing"&&i.Message.Contains("Race 2"));}
 static CompatibilityItem Item(int id=1000)=>new(id,"Sword",21,1,2,50,0,100);
 [Fact] public void ItemComparisonDistinguishesMissingCatalogAndMismatchedValues(){
  Assert.Equal("catalog.missing",Assert.Single(CompatibilityRules.Compare(Item(),null)).Code);
  var issue=Assert.Single(CompatibilityRules.Compare(Item(),Item() with{Damage=60,Name="Custom Sword"}));
  Assert.Equal("catalog.different",issue.Code);Assert.Contains("Damage",issue.Message);Assert.Equal(1000,issue.Record!.Id);
  Assert.Empty(CompatibilityRules.Compare(Item(),Item()));
 }
 [Fact] public void ReportCountsEveryIssueWhileBoundingExamples(){var report=new CompatibilityReportBuilder(2);for(var i=0;i<5;i++)report.Add(new("missing",CompatibilitySeverity.Warning,"Assets","icon"+i,"Missing"));Assert.Equal(5,report.Counts["missing"]);Assert.Equal(2,report.Examples.Count);Assert.Equal(5,report.Total);}
 [Theory][InlineData(0,true)][InlineData(768,true)][InlineData(864,true)][InlineData(1632,true)][InlineData(2016,true)][InlineData(767,false)][InlineData(865,false)][InlineData(2017,false)]
 public void InventoryFormatAcceptsOnlyKnownCompleteLayouts(int bytes,bool expected)=>Assert.Equal(expected,CompatibilityRules.SupportsInventory(bytes));
 [Fact] public void SchemaChecksTypesKeysNullabilityAndRequiredCustomColumns(){
  var expected=new[]{new CompatibilityColumn("Items","Num","int",false,true),new CompatibilityColumn("Items","Name","varchar(50)",false)};
  Assert.Empty(CompatibilityRules.CompareColumns(expected,[expected[0],expected[1] with{StoreType="varchar(100)"}]));
  Assert.Contains(CompatibilityRules.CompareColumns(expected,[expected[0],expected[1] with{StoreType="int"}]),i=>i.Code=="schema.type");
  Assert.Contains(CompatibilityRules.CompareColumns(expected,[expected[0] with{PrimaryKey=false},expected[1]]),i=>i.Code=="schema.key");
  Assert.Contains(CompatibilityRules.CompareColumns(expected,[expected[0],expected[1] with{Nullable=true}]),i=>i.Code=="schema.nullability");
  Assert.Contains(CompatibilityRules.CompareColumns(expected,[expected[0],expected[1],new("Items","Custom","int",false)]),i=>i.Code=="schema.required-extra");
  Assert.Contains(CompatibilityRules.CompareColumns(expected,[expected[0]]),i=>i.Code=="schema.column");
 }
 [Fact] public void ResourceAuditReportsMissingAndUnsafePathsOnce(){
  var report=new CompatibilityReportBuilder();var audit=new CompatibilityResourceAudit(_=>false,report);
  Assert.False(audit.Require("res://assets/items/icons/1.png","Icon"));Assert.False(audit.Require("res://assets/items/icons/1.png","Icon"));audit.Require("res://assets/../../private.txt","Model");
  Assert.Equal(2,report.Total);Assert.Contains(report.Examples,i=>i.Code=="asset.path");Assert.Equal(1,audit.Checked);
 }
 [Fact] public void CommonWarningsDoNotConsumeAllSamplesForOtherCategories(){var report=new CompatibilityReportBuilder(100);for(var i=0;i<200;i++)report.Add(new("asset.missing",CompatibilitySeverity.Warning,"Assets",i.ToString(),"Missing"));report.Add(new("inventory.orphan",CompatibilitySeverity.Warning,"Inventory","1","Orphan"));Assert.Contains(report.Examples,i=>i.Code=="inventory.orphan");Assert.Equal(201,report.Total);}
 [Fact] public void ResourceReferencesIncludeWeaponModelsAndEffectDependencies(){
  var weapon=System.Text.Json.Nodes.JsonNode.Parse("{\"1000\":{\"stem\":\"sword\"}}")!.AsObject();Assert.Contains(CompatibilityResources.References("assets/items/weapon/index.json",weapon),r=>r.Path=="res://assets/items/weapon/sword.glb");
  var effect=System.Text.Json.Nodes.JsonNode.Parse("{\"parts\":[{\"tex\":[\"fire\"],\"meshRef\":\"blade\"}]}")!.AsObject();var references=CompatibilityResources.References("assets/fx/fire.json",effect).ToArray();Assert.Contains(references,r=>r.Path=="res://assets/fx/tex/fire.png");Assert.Contains(references,r=>r.Path=="res://assets/fx/mesh/blade.json");
 }
}
