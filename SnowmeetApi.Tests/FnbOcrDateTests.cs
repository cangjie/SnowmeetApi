using SnowmeetApi.Controllers.Fnb;

namespace SnowmeetApi.Tests;

public class FnbOcrDateTests
{
    [Theory]
    [InlineData("生产日期：2025-08-0512:30")]      // 后面紧跟时间，没有空格
    [InlineData("12025-08-05")]                    // 前面粘着别的数字
    [InlineData("L2025-08-05A1")]                  // 前后都有字母
    [InlineData("2025-08-05")]
    [InlineData("生产日期 2025/08/05 08:30")]
    [InlineData("2025.08.051")]
    public void YyyyMmDdIsRecognizedRegardlessOfPrefixAndSuffix(string line)
    {
        var (all, _) = FnbMaterialController.ExtractDates(new[] { line });
        Assert.Equal("2025-08-05", all.First());
    }

    [Fact]
    public void YyyyMmDdComesBeforeLooserGuessesFromEarlierLines()
    {
        // 「250312」会被当成 6 位喷码 2025-03-12；yyyy-MM-dd 字样要排在它前面
        var (all, _) = FnbMaterialController.ExtractDates(new[] { "批号 250312", "2025-08-05" });
        Assert.Equal("2025-08-05", all.First());
    }

    [Fact]
    public void ExpireLineWithTimeSuffixIsRecognizedAsExpireDate()
    {
        var (_, expire) = FnbMaterialController.ExtractDates(new[] { "保质期至2025-08-0512:00" });
        Assert.Equal("2025-08-05", expire.First());
    }

    [Theory]
    [InlineData("2026年7月16日", "2026-07-16")]
    [InlineData("20260716", "2026-07-16")]
    [InlineData("16 JUL 2026", "2026-07-16")]
    [InlineData("16-07-2026", "2026-07-16")]
    public void OtherFormatsStillWork(string line, string expected)
    {
        var (all, _) = FnbMaterialController.ExtractDates(new[] { line });
        Assert.Equal(expected, all.First());
    }
}
