using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
public class AccountFieldTests {
 [Fact] public void DateFieldsUseUtcAndAllowClearingNullableExpiry(){var field=new FieldDescriptor("PremiumDate","Premium",FieldKind.DateTime,true);Assert.Equal("2030-01-02T01:04:05.0000000Z",FieldValueParser.Parse(field,"2030-01-02T03:04:05+02:00")!.GetValue<string>());Assert.Null(FieldValueParser.Parse(field,""));Assert.Throws<FormatException>(()=>FieldValueParser.Parse(field,"invalid date"));}
}
