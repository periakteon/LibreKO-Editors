using System.Text;
using LibreKO.Editors.Core.Sources;

public class TextDocumentTests
{
    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8bom")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    public void EncodingAndNewlinesSurvive(string kind)
    {
        Encoding encoding = kind switch { "utf8" => new UTF8Encoding(false, true), "utf8bom" => new UTF8Encoding(true, true), "utf16le" => new UnicodeEncoding(false,true,true), _ => new UnicodeEncoding(true,true,true) };
        var input = encoding.GetPreamble().Concat(encoding.GetBytes("Türkçe\r\nsecond\r\n")).ToArray();
        var doc = TextDocument.Decode(input);
        Assert.Equal("\r\n", doc.Newline);
        Assert.Equal(input, doc.Encode(doc.Text));
        Assert.Contains("changed", TextDocument.Decode(doc.Encode(doc.Text + "changed")).Text);
    }

    [Fact]
    public void InvalidAndUnsupportedEncodingsRequireConversion()
    {
        Assert.Throws<InvalidDataException>(() => TextDocument.Decode([0xff, 0xfe, 0, 0, 1, 0, 0, 0]));
        Assert.Throws<InvalidDataException>(() => TextDocument.Decode([0xc3, 0x28]));
    }
    [Fact]
    public void ExplicitConversionProducesStrictUtf8WithoutChangingTheOriginal()
    {
        var encoding=new UTF32Encoding(false,true,true);var input=encoding.GetPreamble().Concat(encoding.GetBytes("[\r\n {\"Name\":\"Türkçe\"}\r\n]")).ToArray();var saved=input.ToArray();
        var converted=TextConversion.ToUtf8(input,12000);Assert.Equal("[\r\n {\"Name\":\"Türkçe\"}\r\n]",TextDocument.Decode(converted).Text);Assert.Equal(saved,input);
        Assert.Throws<InvalidDataException>(()=>TextConversion.ToUtf8([0xc3,0x28],65001));
        Assert.Throws<InvalidDataException>(()=>TextConversion.ToUtf8(input,42));
    }
}
