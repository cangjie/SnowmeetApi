using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;

namespace SnowmeetApi.Services.Share;

/// <summary>向支付宝绑定分账收款方（alipay.trade.royalty.relation.bind）。测试里用假的，不调支付宝。</summary>
public interface IAlipayRoyaltyBinder
{
    /// <param name="accountType">loginName 或 userId</param>
    /// <param name="realName">登录号绑定时核对的真实姓名</param>
    Task<(bool ok, string message)> BindAsync(string accountType, string account, string? realName);
}

public sealed class AlipayRoyaltyBinder(ApplicationDBContext db, IConfiguration config, IHttpContextAccessor http) : IAlipayRoyaltyBinder
{
    public Task<(bool ok, string message)> BindAsync(string accountType, string account, string? realName)
        => Task.FromResult(new AliController(db, config, http).BindRoyaltyReceiver(accountType, account, realName));
}
