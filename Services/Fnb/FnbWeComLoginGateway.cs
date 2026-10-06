using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using SnowmeetApi.Controllers;
using SnowmeetApi.Controllers.Fnb;
using SnowmeetApi.Data;

namespace SnowmeetApi.Services.Fnb;

public interface IFnbWeComLoginGateway { Task<string?> ExchangeCodeAsync(string code); }

// 仅认证请求才访问企业微信。测试注入假网关，不请求外网或真实账号。
public sealed class FnbWeComLoginGateway(ApplicationDBContext db, IConfiguration config) : IFnbWeComLoginGateway
{
    public async Task<string?> ExchangeCodeAsync(string code)
    {
        string batch = Guid.NewGuid().ToString("N");
        var token = await new FnbWeComController(db, config).GetToken(batch, "食材v4登录");
        if (token == null) throw new ArgumentException("企业微信接口调用失败");
        var log = await new MiniAppHelperController(db, config).PerformRequest(
            "https://qyapi.weixin.qq.com/cgi-bin/auth/getuserinfo?access_token=" + Uri.EscapeDataString(token) + "&code=" + Uri.EscapeDataString(code),
            "", "", "GET", "企业微信", "食材v4登录", "OAuth code 换 UserId", batch);
        JObject response;
        try { response = JObject.Parse(log.response); }
        catch (Newtonsoft.Json.JsonException) { throw new ArgumentException("企业微信响应无效"); }
        if ((int?)response["errcode"] != 0) throw new ArgumentException("企业微信授权失败，请重新登录");
        return ((string?)response["userid"])?.Trim();
    }
}
