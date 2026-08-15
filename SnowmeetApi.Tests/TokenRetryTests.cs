using SnowmeetApi.Helpers;
using Xunit;

namespace SnowmeetApi.Tests
{
    // access_token 失效的识别。
    // 背景：token 走本地文件缓存、只按 1 小时到期判断，但同一个 appid 被多方获取时
    // （本项目两台服务器各自部署，另有 legacy get_token.aspx），后取的会让先取的失效——
    // 缓存这边不知情，继续用就是 42001。所以要能从响应里认出"token 不行了"并强制刷新重试。
    public class TokenRetryTests
    {
        [Fact]
        public void token过期要重试()
        {
            Assert.True(SubscribeMessageHelper.IsTokenInvalidResponse(
                "{\"errcode\":42001,\"errmsg\":\"access_token expired rid: 6a8024e7\"}"));
        }

        [Fact]
        public void 凭证无效要重试()
        {
            Assert.True(SubscribeMessageHelper.IsTokenInvalidResponse("{\"errcode\":40001,\"errmsg\":\"invalid credential\"}"));
        }

        [Fact]
        public void token非法要重试()
        {
            Assert.True(SubscribeMessageHelper.IsTokenInvalidResponse("{\"errcode\":40014,\"errmsg\":\"invalid access_token\"}"));
        }

        [Fact]
        public void 发送成功不重试()
        {
            Assert.False(SubscribeMessageHelper.IsTokenInvalidResponse("{\"errcode\":0,\"errmsg\":\"ok\"}"));
        }

        [Fact]
        public void 用户没订阅额度是业务问题不重试()
        {
            Assert.False(SubscribeMessageHelper.IsTokenInvalidResponse(
                "{\"errcode\":43101,\"errmsg\":\"user refuse to accept the msg\"}"));
        }

        [Fact]
        public void 参数不符模板是业务问题不重试()
        {
            Assert.False(SubscribeMessageHelper.IsTokenInvalidResponse("{\"errcode\":47003,\"errmsg\":\"argument invalid\"}"));
        }

        [Fact]
        public void 空响应不重试()
        {
            Assert.False(SubscribeMessageHelper.IsTokenInvalidResponse(null));
            Assert.False(SubscribeMessageHelper.IsTokenInvalidResponse(""));
        }

        [Fact]
        public void 非JSON响应不重试而不是抛异常()
        {
            Assert.False(SubscribeMessageHelper.IsTokenInvalidResponse("<html>502 Bad Gateway</html>"));
        }
    }
}
