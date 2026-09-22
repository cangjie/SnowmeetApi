using System;
using System.Text;

namespace SnowmeetApi.Services.Fnb;

public static class FnbText
{
    static FnbText() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static bool FitsChineseVarchar(string? value, int maxBytes)
    {
        if (value == null) return true;
        try
        {
            var gbk = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            return gbk.GetByteCount(value) <= maxBytes;
        }
        catch (EncoderFallbackException) { return false; }
    }
}
