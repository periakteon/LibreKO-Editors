using System.Text;
namespace LibreKO.Editors.Core.Sources;
public static class TextConversion
{
    public static byte[] ToUtf8(byte[] original,int sourceCodePage)
    {
        SourceLimits.CheckedLength(original.LongLength);
        if(sourceCodePage is not (65001 or 1200 or 1201 or 12000 or 12001 or 1252 or 1254))throw new InvalidDataException("Select an explicitly supported source encoding.");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding=Encoding.GetEncoding(sourceCodePage,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback);
        try
        {
            var preamble=encoding.GetPreamble();int skip=preamble.Length>0&&original.AsSpan().StartsWith(preamble)?preamble.Length:0;
            var text=encoding.GetString(original,skip,original.Length-skip);if(text.StartsWith('\ufeff'))text=text[1..];
            if(text.Contains('\0'))throw new InvalidDataException("Decoded text contains NUL; the selected encoding does not match this source.");
            return new UTF8Encoding(false,true).GetBytes(text);
        }
        catch(DecoderFallbackException e){throw new InvalidDataException("The source is invalid in the explicitly selected encoding.",e);}
    }
}
