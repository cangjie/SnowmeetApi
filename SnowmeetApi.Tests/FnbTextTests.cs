using SnowmeetApi.Services.Fnb;

namespace SnowmeetApi.Tests;

public class FnbTextTests
{
    [Fact]
    public void ChineseVarcharAcceptsChineseAndRejectsUnrepresentableText()
    {
        Assert.True(FnbText.FitsChineseVarchar("高筋面粉", 20));
        Assert.False(FnbText.FitsChineseVarchar("面粉😀", 20));
        Assert.False(FnbText.FitsChineseVarchar(new string('中', 11), 20));
    }
}
