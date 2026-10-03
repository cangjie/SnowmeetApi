using SnowmeetApi.Controllers.Fnb;

namespace SnowmeetApi.Tests;

public class WeComJsSdkSignatureTests
{
    [Fact]
    public void SignatureMatchesOfficialJsSdkExample()
    {
        // 微信 JS-SDK 文档「附录1-JS-SDK使用权限签名算法」的示例；企业微信沿用同一算法
        string signature = FnbWeComController.ComputeJsSdkSignature(
            "sM4AOVdWfPE4DxkXGEs8VMCPGGVi4C3VM0P37wVUCFvkVAy_90u5h9nbSlYy3-Sl-HhTdfl2fzFy1AOcHKP7qg",
            "Wm3WZYTPz0wzccnW", 1414587457, "http://mp.weixin.qq.com?params=value");
        Assert.Equal("0f9de62fce790f9a083d5c99e95740ceb90c27ed", signature);
    }

    [Theory]
    [InlineData("https://mini.snowmeet.top/wecom/ble_print_test/index.html#top", "https://mini.snowmeet.top/wecom/ble_print_test/index.html")]
    [InlineData("https://mini.snowmeet.top/a.html?x=1&y=2", "https://mini.snowmeet.top/a.html?x=1&y=2")]
    [InlineData("https://MINI.snowmeet.top/a.html", "https://MINI.snowmeet.top/a.html")]
    public void UrlOnTrustedHostIsSignedWithoutHash(string url, string expected)
    {
        Assert.Equal(expected, FnbWeComController.NormalizeJsSdkUrl(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/wecom/ble_print_test/index.html")]
    [InlineData("http://mini.snowmeet.top/a.html")]
    [InlineData("https://mini.snowmeet.top:8443/a.html")]
    [InlineData("https://mini.snowmeet.com/a.html")]
    [InlineData("https://evil.example.com/?u=https://mini.snowmeet.top/")]
    [InlineData("https://mini.snowmeet.top.evil.example.com/a.html")]
    public void UrlOutsideTrustedHostIsRejected(string? url)
    {
        Assert.Null(FnbWeComController.NormalizeJsSdkUrl(url));
    }
}
