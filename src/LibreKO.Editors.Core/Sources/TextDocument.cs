using System.Text;

namespace LibreKO.Editors.Core.Sources;

public sealed class TextDocument
{
    readonly Encoding encoding;
    readonly byte[] preamble;
    public string Text { get; }
    public string EncodingName => encoding.WebName;
    public bool HasBom => preamble.Length != 0;
    public string Newline { get; }
    TextDocument(string text, Encoding encoding, byte[] preamble)
    {
        Text=text; this.encoding=encoding; this.preamble=preamble;
        var lf=text.IndexOf('\n'); Newline=lf>0 && text[lf-1]=='\r' ? "\r\n" : lf>=0 ? "\n" : text.Contains('\r') ? "\r" : Environment.NewLine;
    }
    public static TextDocument Decode(byte[] bytes)
    {
        SourceLimits.CheckedLength(bytes.LongLength);
        if(bytes.AsSpan().StartsWith(new byte[]{0xff,0xfe,0,0}) || bytes.AsSpan().StartsWith(new byte[]{0,0,0xfe,0xff}))
            throw new InvalidDataException("UTF-32 requires an explicit backed-up conversion to UTF-8 or UTF-16.");
        Encoding enc; int skip;
        if(bytes.AsSpan().StartsWith(new byte[]{0xef,0xbb,0xbf})) { enc=new UTF8Encoding(false,true); skip=3; }
        else if(bytes.AsSpan().StartsWith(new byte[]{0xff,0xfe})) { enc=new UnicodeEncoding(false,false,true); skip=2; }
        else if(bytes.AsSpan().StartsWith(new byte[]{0xfe,0xff})) { enc=new UnicodeEncoding(true,false,true); skip=2; }
        else { enc=new UTF8Encoding(false,true); skip=0; }
        try { var text=enc.GetString(bytes,skip,bytes.Length-skip); if(text.Contains('\0')) throw new InvalidDataException("NUL text is unsupported; select an explicit encoding conversion."); return new(text,enc,bytes[..skip]); }
        catch(DecoderFallbackException e) { throw new InvalidDataException("Invalid text encoding; an explicit backed-up conversion is required.",e); }
    }
    // Existing mixed newline tokens remain untouched; callers use Newline when inserting lines.
    public byte[] Encode(string text) => preamble.Concat(encoding.GetBytes(text)).ToArray();
}
